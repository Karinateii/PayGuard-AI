using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Delivers outbound webhook events to customer-configured endpoints.
/// - HMAC-SHA256 signs every payload with the endpoint's signing secret
/// - Retries with exponential backoff (1s, 2s, 4s…)
/// - Updates delivery status on the WebhookEndpoint entity
/// - Fire-and-forget: failures are logged but never block the caller
/// </summary>
public class WebhookDeliveryService : IWebhookDeliveryService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookDeliveryService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public WebhookDeliveryService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        HttpClient httpClient,
        ILogger<WebhookDeliveryService> logger)
    {
        _dbFactory = dbFactory;
        _httpClient = httpClient;
        _logger = logger;

        // Sensible default timeout for outbound calls
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task DeliverEventAsync(
        string tenantId,
        string eventType,
        object payload,
        CancellationToken cancellationToken = default)
    {
        // Find all active endpoints for this tenant that are subscribed to this event type
        List<WebhookEndpoint> endpoints;
        await using (var db = await _dbFactory.CreateDbContextAsync(cancellationToken))
        {
            db.SetTenantId(tenantId);
            endpoints = await db.WebhookEndpoints
                .Where(e => e.IsActive)
                .ToListAsync(cancellationToken);
        }

        // Filter by event subscription
        var matchingEndpoints = endpoints
            .Where(e => e.Events.Contains(eventType))
            .ToList();

        if (matchingEndpoints.Count == 0)
        {
            _logger.LogDebug("No active webhook endpoints for tenant {TenantId} / event {EventType}",
                tenantId, eventType);
            return;
        }

        _logger.LogInformation(
            "Delivering webhook event {EventType} to {Count} endpoint(s) for tenant {TenantId}",
            eventType, matchingEndpoints.Count, tenantId);

        // Build the event envelope once
        var envelope = new WebhookEventEnvelope
        {
            Id = Guid.NewGuid().ToString("N"),
            Event = eventType,
            Timestamp = DateTime.UtcNow,
            TenantId = tenantId,
            Data = payload
        };

        var jsonPayload = JsonSerializer.Serialize(envelope, JsonOptions);

        // Deliver to each endpoint concurrently
        var deliveryTasks = matchingEndpoints.Select(ep =>
            DeliverToEndpointAsync(ep, jsonPayload, cancellationToken));

        await Task.WhenAll(deliveryTasks);
    }

    private async Task DeliverToEndpointAsync(
        WebhookEndpoint endpoint,
        string jsonPayload,
        CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(1, endpoint.MaxRetries);
        string? lastStatus = null;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Sign the payload with HMAC-SHA256
                var signature = ComputeHmacSha256(jsonPayload, endpoint.SigningSecret);

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url);
                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                request.Headers.Add("X-PayGuard-Signature", signature);
                request.Headers.Add("X-PayGuard-Event", endpoint.Events.FirstOrDefault() ?? "unknown");
                request.Headers.Add("X-PayGuard-Delivery", Guid.NewGuid().ToString("N"));
                request.Headers.Add("User-Agent", "PayGuardAI-Webhook/1.0");

                var response = await _httpClient.SendAsync(request, cancellationToken);

                lastStatus = $"{(int)response.StatusCode} {response.ReasonPhrase}";

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "Webhook delivered to {Url} — {Status} (attempt {Attempt})",
                        endpoint.Url, lastStatus, attempt);
                    await UpdateDeliveryStatusAsync(endpoint.Id, endpoint.TenantId, lastStatus, cancellationToken);
                    return;
                }

                _logger.LogWarning(
                    "Webhook delivery to {Url} returned {Status} (attempt {Attempt}/{Max})",
                    endpoint.Url, lastStatus, attempt, maxRetries);
            }
            catch (TaskCanceledException)
            {
                lastStatus = "timeout";
                _logger.LogWarning(
                    "Webhook delivery to {Url} timed out (attempt {Attempt}/{Max})",
                    endpoint.Url, attempt, maxRetries);
            }
            catch (HttpRequestException ex)
            {
                lastStatus = $"error: {ex.Message}";
                _logger.LogWarning(ex,
                    "Webhook delivery to {Url} failed (attempt {Attempt}/{Max})",
                    endpoint.Url, attempt, maxRetries);
            }

            // Exponential backoff before retry (1s, 2s, 4s, 8s…)
            if (attempt < maxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                await Task.Delay(delay, cancellationToken);
            }
        }

        // All retries exhausted
        _logger.LogError(
            "Webhook delivery to {Url} failed after {MaxRetries} attempts — last status: {Status}",
            endpoint.Url, maxRetries, lastStatus);
        await UpdateDeliveryStatusAsync(endpoint.Id, endpoint.TenantId, lastStatus ?? "failed", cancellationToken);
    }

    private async Task UpdateDeliveryStatusAsync(
        Guid endpointId, string tenantId, string status, CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.SetTenantId(tenantId);

            var endpoint = await db.WebhookEndpoints.FindAsync([endpointId], ct);
            if (endpoint != null)
            {
                endpoint.LastDeliveryAt = DateTime.UtcNow;
                endpoint.LastDeliveryStatus = status;
                endpoint.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            // Status tracking should never crash the pipeline
            _logger.LogWarning(ex, "Failed to update webhook delivery status for endpoint {EndpointId}", endpointId);
        }
    }

    private static string ComputeHmacSha256(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return $"sha256={Convert.ToHexStringLower(hash)}";
    }

    /// <summary>
    /// The JSON envelope wrapping every outbound webhook event.
    /// </summary>
    private sealed class WebhookEventEnvelope
    {
        public string Id { get; set; } = string.Empty;
        public string Event { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string TenantId { get; set; } = string.Empty;
        public object Data { get; set; } = new();
    }
}
