using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Wise (TransferWise) payment provider implementation.
/// Implements unified IPaymentProvider interface for Wise API integration.
/// Supports webhook normalization, RSA-SHA256 signature verification, and exchange rates.
/// </summary>
public class WiseProvider : IPaymentProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WiseProvider> _logger;
    private readonly IMemoryCache _cache;
    private readonly string _apiToken;
    private readonly string _baseUrl;
    private readonly string _webhookPublicKey;
    private readonly string _profileId;

    public string ProviderName => "wise";

    public WiseProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<WiseProvider> logger,
        IMemoryCache cache)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cache = cache;

        _apiToken = configuration["Wise:ApiToken"] ?? "";
        _baseUrl = configuration["Wise:BaseUrl"] ?? "https://api.transferwise.com";
        _webhookPublicKey = configuration["Wise:WebhookPublicKey"] ?? "";
        _profileId = configuration["Wise:ProfileId"] ?? "";

        _httpClient.BaseAddress = new Uri(_baseUrl);
        if (!string.IsNullOrEmpty(_apiToken))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiToken}");
        }
    }

    public async Task<NormalizedTransaction> NormalizeWebhookAsync(object webhookPayload)
    {
        try
        {
            var json = webhookPayload is string str ? str : JsonSerializer.Serialize(webhookPayload);
            var payload = JsonSerializer.Deserialize<WiseWebhookPayload>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload?.Data == null)
            {
                throw new ArgumentException("Invalid Wise webhook payload");
            }

            var data = payload.Data;
            var resource = data.Resource;

            return new NormalizedTransaction
            {
                TransactionId = resource?.Id?.ToString() ?? data.TransferId?.ToString() ?? Guid.NewGuid().ToString(),
                Provider = ProviderName,
                CustomerId = resource?.ProfileId?.ToString() ?? _profileId,
                CustomerEmail = data.CustomerEmail,
                SourceCurrency = data.SourceCurrency ?? resource?.SourceCurrency ?? "USD",
                SourceAmount = data.SourceAmount ?? resource?.SourceAmount ?? 0,
                DestinationCurrency = data.TargetCurrency ?? resource?.TargetCurrency ?? "USD",
                DestinationAmount = data.TargetAmount ?? resource?.TargetAmount ?? 0,
                SourceCountry = InferCountryFromCurrency(data.SourceCurrency ?? resource?.SourceCurrency),
                DestinationCountry = InferCountryFromCurrency(data.TargetCurrency ?? resource?.TargetCurrency),
                Status = NormalizeStatus(data.CurrentState ?? payload.EventType ?? "unknown"),
                CreatedAt = data.OccurredAt ?? DateTime.UtcNow,
                CompletedAt = IsCompletedState(data.CurrentState) ? data.OccurredAt : null,
                Description = $"Wise transfer {resource?.Id}",
                Metadata = new Dictionary<string, object>
                {
                    { "originalEvent", payload.EventType ?? "unknown" },
                    { "provider", ProviderName },
                    { "currentState", data.CurrentState ?? "unknown" },
                    { "previousState", data.PreviousState ?? "" },
                    { "transferId", resource?.Id?.ToString() ?? "" }
                }
            };
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            _logger.LogError(ex, "Failed to normalize Wise webhook");
            throw;
        }
    }

    public bool VerifyWebhookSignature(string payload, string signature)
    {
        if (string.IsNullOrEmpty(_webhookPublicKey))
        {
            // SECURITY: fail-closed — reject if no public key configured
            _logger.LogWarning("Wise webhook rejected — webhook public key not configured");
            return false;
        }

        try
        {
            // Wise uses RSA-SHA256 signature verification
            // The signature header contains a base64-encoded RSA-SHA256 signature
            var signatureBytes = Convert.FromBase64String(signature);
            var payloadBytes = Encoding.UTF8.GetBytes(payload);

            using var rsa = RSA.Create();
            // Import the PEM public key
            var publicKeyPem = _webhookPublicKey;
            if (publicKeyPem.Contains("BEGIN PUBLIC KEY"))
            {
                rsa.ImportFromPem(publicKeyPem);
            }
            else
            {
                // Assume it's a base64-encoded DER key
                var keyBytes = Convert.FromBase64String(publicKeyPem);
                rsa.ImportSubjectPublicKeyInfo(keyBytes, out _);
            }

            var isValid = rsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            if (!isValid)
            {
                _logger.LogWarning("Wise webhook RSA-SHA256 signature verification failed");
            }

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify Wise webhook signature");
            return false;
        }
    }

    public async Task<decimal> GetExchangeRateAsync(string fromCurrency, string toCurrency, decimal amount)
    {
        var cacheKey = $"wise_rate_{fromCurrency}_{toCurrency}";

        if (_cache.TryGetValue<decimal>(cacheKey, out var cachedRate))
        {
            _logger.LogDebug("Exchange rate cache hit: {From} -> {To}", fromCurrency, toCurrency);
            return cachedRate;
        }

        try
        {
            // Wise API: GET /v1/rates?source={from}&target={to}
            var response = await _httpClient.GetFromJsonAsync<List<WiseExchangeRate>>(
                $"/v1/rates?source={fromCurrency}&target={toCurrency}");

            if (response != null && response.Count > 0)
            {
                var rate = response[0].Rate;
                _cache.Set(cacheKey, rate, TimeSpan.FromMinutes(30));
                return rate;
            }

            _logger.LogWarning("Failed to get exchange rate from Wise API");
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting exchange rate from Wise");
            return 0;
        }
    }

    public bool IsConfigured()
    {
        return !string.IsNullOrEmpty(_apiToken);
    }

    /// <summary>
    /// Maps Wise transfer states to normalized status values.
    /// Wise states: incoming_payment_waiting, processing, funds_converted,
    /// outgoing_payment_sent, bounced_back, cancelled, funds_refunded
    /// </summary>
    public static string NormalizeStatus(string wiseState)
    {
        return wiseState.ToLowerInvariant() switch
        {
            "outgoing_payment_sent" => "COMPLETED",
            "funds_converted" => "PROCESSING",
            "processing" => "PROCESSING",
            "incoming_payment_waiting" => "PENDING",
            "waiting_recipient_input_to_proceed" => "PENDING",
            "bounced_back" => "FAILED",
            "funds_refunded" => "FAILED",
            "cancelled" => "CANCELLED",
            "charged_back" => "CANCELLED",
            "transfer_state_change" => "PROCESSING",
            "transfers#state-change" => "PROCESSING",
            _ => "PENDING"
        };
    }

    /// <summary>
    /// Check if the given state represents a completed transfer.
    /// </summary>
    public static bool IsCompletedState(string? state)
    {
        return state?.ToLowerInvariant() == "outgoing_payment_sent";
    }

    /// <summary>
    /// Infer ISO country code from ISO currency code.
    /// Covers major Wise-supported corridors.
    /// </summary>
    public static string InferCountryFromCurrency(string? currency)
    {
        if (string.IsNullOrEmpty(currency)) return "US";

        return currency.ToUpperInvariant() switch
        {
            "USD" => "US",
            "GBP" => "GB",
            "EUR" => "DE",
            "NGN" => "NG",
            "KES" => "KE",
            "GHS" => "GH",
            "ZAR" => "ZA",
            "CAD" => "CA",
            "AUD" => "AU",
            "JPY" => "JP",
            "INR" => "IN",
            "BRL" => "BR",
            "TZS" => "TZ",
            "UGX" => "UG",
            "XOF" => "SN",
            "EGP" => "EG",
            _ => "US"
        };
    }
}

#region Wise Models

/// <summary>
/// Root Wise webhook payload.
/// Wise sends events like transfers#state-change, transfers#active-cases, etc.
/// </summary>
internal class WiseWebhookPayload
{
    [JsonPropertyName("event_type")]
    public string? EventType { get; set; }

    [JsonPropertyName("schema_version")]
    public string? SchemaVersion { get; set; }

    [JsonPropertyName("sent_at")]
    public DateTime? SentAt { get; set; }

    public WiseWebhookData? Data { get; set; }
}

/// <summary>
/// Wise webhook event data containing transfer details.
/// </summary>
internal class WiseWebhookData
{
    public WiseResource? Resource { get; set; }

    [JsonPropertyName("transfer_id")]
    public long? TransferId { get; set; }

    [JsonPropertyName("current_state")]
    public string? CurrentState { get; set; }

    [JsonPropertyName("previous_state")]
    public string? PreviousState { get; set; }

    [JsonPropertyName("occurred_at")]
    public DateTime? OccurredAt { get; set; }

    [JsonPropertyName("source_currency")]
    public string? SourceCurrency { get; set; }

    [JsonPropertyName("source_amount")]
    public decimal? SourceAmount { get; set; }

    [JsonPropertyName("target_currency")]
    public string? TargetCurrency { get; set; }

    [JsonPropertyName("target_amount")]
    public decimal? TargetAmount { get; set; }

    [JsonPropertyName("customer_email")]
    public string? CustomerEmail { get; set; }
}

/// <summary>
/// Wise resource reference in webhook payload.
/// </summary>
internal class WiseResource
{
    public long? Id { get; set; }

    [JsonPropertyName("profile_id")]
    public long? ProfileId { get; set; }

    [JsonPropertyName("account_id")]
    public long? AccountId { get; set; }

    public string? Type { get; set; }

    [JsonPropertyName("source_currency")]
    public string? SourceCurrency { get; set; }

    [JsonPropertyName("source_amount")]
    public decimal? SourceAmount { get; set; }

    [JsonPropertyName("target_currency")]
    public string? TargetCurrency { get; set; }

    [JsonPropertyName("target_amount")]
    public decimal? TargetAmount { get; set; }
}

/// <summary>
/// Wise exchange rate API response model.
/// </summary>
internal class WiseExchangeRate
{
    public decimal Rate { get; set; }
    public string? Source { get; set; }
    public string? Target { get; set; }
    public DateTime? Time { get; set; }
}

#endregion
