using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Sends structured Slack alerts for critical compliance events.
/// Feature flag: FeatureFlags:SlackAlertsEnabled
/// Config key:   Slack:WebhookUrl
///
/// Fires on:
///   - Critical transactions (score > 75)
///   - High-risk transactions flagged for review (score > 50)
///   - System errors that need human attention
/// </summary>
public class SlackAlertService : IAlertingService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<SlackAlertService> _logger;
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    // Colour bars for Slack attachments (matches PayGuard risk levels)
    private const string ColourCritical = "#D32F2F";  // red
    private const string ColourHigh     = "#F57C00";  // orange
    private const string ColourMedium   = "#FBC02D";  // yellow
    private const string ColourInfo     = "#1976D2";  // blue

    public SlackAlertService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<SlackAlertService> logger,
        IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Send a plain-text alert. Parses the message for known keywords
    /// (Critical / High / Error) to pick the right colour and emoji.
    /// </summary>
    public async Task AlertAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled())
        {
            _logger.LogInformation("Slack alerts disabled. Message: {Message}", message);
            return;
        }

        var (colour, emoji) = message.Contains("Critical", StringComparison.OrdinalIgnoreCase)
            ? (ColourCritical, "🚨")
            : message.Contains("High", StringComparison.OrdinalIgnoreCase)
                ? (ColourHigh, "⚠️")
                : message.Contains("Error", StringComparison.OrdinalIgnoreCase)
                    ? (ColourCritical, "❌")
                    : (ColourInfo, "ℹ️");

        await SendSlackMessageAsync(emoji, message, colour, cancellationToken);
    }

    /// <summary>
    /// Send a rich Slack alert for a specific transaction risk event.
    /// Called from RiskScoringService with full transaction context.
    /// </summary>
    public async Task AlertTransactionAsync(
        string tenantId,
        string externalId,
        int riskScore,
        string riskLevel,
        decimal amount,
        string currency,
        string senderId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled()) return;

        var (colour, emoji) = riskLevel.ToUpperInvariant() switch
        {
            "CRITICAL" => (ColourCritical, "🚨"),
            "HIGH"     => (ColourHigh,     "⚠️"),
            _          => (ColourMedium,   "🟡")
        };

        var liveUrl = _config["AppUrl"] ?? "https://payguard-ai-production.up.railway.app";
        var reviewUrl = $"{liveUrl}/reviews";

        var payload = new
        {
            text = $"{emoji} *PayGuard AI — {riskLevel} Risk Transaction*",
            attachments = new[]
            {
                new
                {
                    color = colour,
                    fields = new[]
                    {
                        new { title = "Tenant",      value = tenantId,             @short = true },
                        new { title = "Transaction", value = externalId,           @short = true },
                        new { title = "Risk Score",  value = $"{riskScore}/100",   @short = true },
                        new { title = "Risk Level",  value = riskLevel,            @short = true },
                        new { title = "Amount",      value = $"{amount:N2} {currency}", @short = true },
                        new { title = "Sender",      value = senderId,             @short = true },
                    },
                    actions = new[]
                    {
                        new { type = "button", text = "Review Now", url = reviewUrl, style = "danger" }
                    },
                    footer = "PayGuard AI",
                    ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        await PostToSlackAsync(json, cancellationToken, tenantId);
    }

    private async Task SendSlackMessageAsync(
        string emoji,
        string text,
        string colour,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            text = $"{emoji} *PayGuard AI Alert*",
            attachments = new[]
            {
                new
                {
                    color = colour,
                    text,
                    footer = "PayGuard AI",
                    ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        await PostToSlackAsync(json, cancellationToken);
    }

    private async Task PostToSlackAsync(string json, CancellationToken cancellationToken, string? tenantId = null)
    {
        var webhookUrl = await ResolveWebhookUrlAsync(tenantId, cancellationToken);
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            _logger.LogWarning("No Slack webhook URL configured (tenant: {TenantId}). Alert skipped.", tenantId ?? "global");
            return;
        }

        try
        {
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(webhookUrl, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Slack alert failed: {StatusCode} — {Body}", response.StatusCode, body);
            }
            else
            {
                _logger.LogInformation("Slack alert sent successfully");
            }
        }
        catch (Exception ex)
        {
            // Never let a Slack failure crash the main flow
            _logger.LogError(ex, "Exception sending Slack alert — alert suppressed to avoid affecting transaction processing");
        }
    }

    private bool IsEnabled() =>
        bool.TryParse(_config["FeatureFlags:SlackAlertsEnabled"], out var result) && result;

    /// <summary>
    /// Resolves the Slack webhook URL for the given tenant.
    /// Priority: tenant OrganizationSettings.SlackWebhookUrl → global Slack:WebhookUrl config.
    /// </summary>
    private async Task<string?> ResolveWebhookUrlAsync(string? tenantId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                var settings = await db.OrganizationSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

                if (!string.IsNullOrWhiteSpace(settings?.SlackWebhookUrl))
                    return settings.SlackWebhookUrl;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not look up tenant Slack webhook for {TenantId}; falling back to global", tenantId);
            }
        }

        return _config["Slack:WebhookUrl"];
    }
}
