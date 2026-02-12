using System.Text.Json;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Afriex payment provider implementation
/// Wraps AfriexApiService and implements unified IPaymentProvider interface
/// </summary>
public class AfriexProvider : IPaymentProvider
{
    private readonly IAfriexApiService _afriexApi;
    private readonly IWebhookSignatureService _signatureService;
    private readonly ILogger<AfriexProvider> _logger;

    public string ProviderName => "afriex";

    public AfriexProvider(
        IAfriexApiService afriexApi,
        IWebhookSignatureService signatureService,
        ILogger<AfriexProvider> logger)
    {
        _afriexApi = afriexApi;
        _signatureService = signatureService;
        _logger = logger;
    }

    public async Task<NormalizedTransaction> NormalizeWebhookAsync(object webhookPayload)
    {
        try
        {
            // Parse Afriex webhook payload
            var json = webhookPayload is string str ? str : JsonSerializer.Serialize(webhookPayload);
            var payload = JsonSerializer.Deserialize<AfriexWebhookPayload>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload?.Data == null)
            {
                throw new ArgumentException("Invalid Afriex webhook payload");
            }

            var data = payload.Data;

            // Map to normalized format
            return new NormalizedTransaction
            {
                TransactionId = data.TransactionId ?? data.Id ?? Guid.NewGuid().ToString(),
                Provider = ProviderName,
                CustomerId = data.CustomerId ?? "unknown",
                CustomerEmail = data.CustomerEmail,
                SourceCurrency = data.SourceCurrency ?? "USD",
                SourceAmount = data.SourceAmount ?? 0,
                DestinationCurrency = data.DestinationCurrency ?? "NGN",
                DestinationAmount = data.DestinationAmount ?? 0,
                SourceCountry = data.SourceCountry ?? "US",
                DestinationCountry = data.DestinationCountry ?? "NG",
                Status = NormalizeStatus(data.Status ?? "PENDING"),
                CreatedAt = data.CreatedAt ?? DateTime.UtcNow,
                CompletedAt = data.CompletedAt,
                Description = data.Description,
                Metadata = new Dictionary<string, object>
                {
                    { "originalEvent", payload.Event ?? "unknown" },
                    { "provider", ProviderName }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to normalize Afriex webhook");
            throw;
        }
    }

    public bool VerifyWebhookSignature(string payload, string signature)
    {
        return _signatureService.VerifySignature(payload, signature);
    }

    public async Task<decimal> GetExchangeRateAsync(string fromCurrency, string toCurrency, decimal amount)
    {
        try
        {
            var response = await _afriexApi.GetExchangeRateAsync(fromCurrency, toCurrency, amount);
            return response?.Rate ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get exchange rate from Afriex");
            return 0;
        }
    }

    public bool IsConfigured()
    {
        // Check if Afriex API is configured (has API key)
        return !string.IsNullOrEmpty(_afriexApi.GetType().GetField("_apiKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(_afriexApi) as string);
    }

    private static string NormalizeStatus(string afriexStatus)
    {
        return afriexStatus.ToUpperInvariant() switch
        {
            "PENDING" => "PENDING",
            "PROCESSING" => "PROCESSING",
            "COMPLETED" => "COMPLETED",
            "SUCCESS" => "COMPLETED",
            "FAILED" => "FAILED",
            "REJECTED" => "REJECTED",
            "CANCELLED" => "CANCELLED",
            _ => "PENDING"
        };
    }
}

/// <summary>
/// Afriex webhook payload structure
/// </summary>
internal class AfriexWebhookPayload
{
    public string? Event { get; set; }
    public AfriexWebhookData? Data { get; set; }
}

internal class AfriexWebhookData
{
    public string? TransactionId { get; set; }
    public string? Id { get; set; }
    public string? CustomerId { get; set; }
    public string? CustomerEmail { get; set; }
    public string? SourceCurrency { get; set; }
    public decimal? SourceAmount { get; set; }
    public string? DestinationCurrency { get; set; }
    public decimal? DestinationAmount { get; set; }
    public string? SourceCountry { get; set; }
    public string? DestinationCountry { get; set; }
    public string? Status { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Description { get; set; }
}
