using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Flutterwave payment provider implementation
/// Implements unified IPaymentProvider interface for Flutterwave API integration
/// </summary>
public class FlutterwaveProvider : IPaymentProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FlutterwaveProvider> _logger;
    private readonly IMemoryCache _cache;
    private readonly string _secretKey;
    private readonly string _baseUrl;
    private readonly string _webhookSecretHash;

    public string ProviderName => "flutterwave";

    public FlutterwaveProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<FlutterwaveProvider> logger,
        IMemoryCache cache)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cache = cache;

        // Get from configuration
        _secretKey = configuration["Flutterwave:SecretKey"] ?? "";
        _baseUrl = configuration["Flutterwave:BaseUrl"] ?? "https://api.flutterwave.com/v3";
        _webhookSecretHash = configuration["Flutterwave:WebhookSecretHash"] ?? "";

        _httpClient.BaseAddress = new Uri(_baseUrl);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_secretKey}");
    }

    public async Task<NormalizedTransaction> NormalizeWebhookAsync(object webhookPayload)
    {
        try
        {
            // Parse Flutterwave webhook payload
            var json = webhookPayload is string str ? str : JsonSerializer.Serialize(webhookPayload);
            var payload = JsonSerializer.Deserialize<FlutterwaveWebhookPayload>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload?.Data == null)
            {
                throw new ArgumentException("Invalid Flutterwave webhook payload");
            }

            var data = payload.Data;

            // Map to normalized format
            return new NormalizedTransaction
            {
                TransactionId = data.TxRef ?? data.FlwRef ?? data.Id?.ToString() ?? Guid.NewGuid().ToString(),
                Provider = ProviderName,
                CustomerId = data.Customer?.Id?.ToString() ?? data.Customer?.Email ?? "unknown",
                CustomerEmail = data.Customer?.Email,
                SourceCurrency = data.Currency ?? "USD",
                SourceAmount = data.Amount ?? 0,
                DestinationCurrency = data.DestinationCurrency ?? data.Currency ?? "NGN",
                DestinationAmount = data.AmountSettled ?? data.Amount ?? 0,
                SourceCountry = ExtractCountryCode(data.PaymentType) ?? "US",
                DestinationCountry = data.Customer?.Country ?? "NG",
                Status = NormalizeStatus(data.Status ?? "pending"),
                CreatedAt = data.CreatedAt ?? DateTime.UtcNow,
                CompletedAt = data.Status?.ToLower() == "successful" ? DateTime.UtcNow : null,
                Description = data.Narration,
                Metadata = new Dictionary<string, object>
                {
                    { "originalEvent", payload.Event ?? "unknown" },
                    { "provider", ProviderName },
                    { "paymentType", data.PaymentType ?? "unknown" },
                    { "flwRef", data.FlwRef ?? "" }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to normalize Flutterwave webhook");
            throw;
        }
    }

    public bool VerifyWebhookSignature(string payload, string signature)
    {
        if (string.IsNullOrEmpty(_webhookSecretHash))
        {
            _logger.LogWarning("Flutterwave webhook secret hash not configured, skipping verification");
            return true; // Allow in development
        }

        try
        {
            // Flutterwave uses SHA256 HMAC for webhook verification
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_webhookSecretHash));
            var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var computedSignature = BitConverter.ToString(computedHash).Replace("-", "").ToLower();

            return signature.ToLower() == computedSignature;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify Flutterwave webhook signature");
            return false;
        }
    }

    public async Task<decimal> GetExchangeRateAsync(string fromCurrency, string toCurrency, decimal amount)
    {
        var cacheKey = $"flw_rate_{fromCurrency}_{toCurrency}";

        if (_cache.TryGetValue<decimal>(cacheKey, out var cachedRate))
        {
            _logger.LogDebug("Exchange rate cache hit: {From} -> {To}", fromCurrency, toCurrency);
            return cachedRate;
        }

        try
        {
            // Flutterwave doesn't have a direct exchange rate endpoint
            // Calculate based on transfer quote
            var response = await _httpClient.GetFromJsonAsync<FlutterwaveTransferResponse>(
                $"/transfers/rates?amount={amount}&destination_currency={toCurrency}&source_currency={fromCurrency}");

            if (response?.Status == "success" && response.Data != null)
            {
                var rate = response.Data.Rate;
                _cache.Set(cacheKey, rate, TimeSpan.FromMinutes(30));
                return rate;
            }

            _logger.LogWarning("Failed to get exchange rate from Flutterwave");
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting exchange rate from Flutterwave");
            return 0;
        }
    }

    public bool IsConfigured()
    {
        return !string.IsNullOrEmpty(_secretKey);
    }

    private static string NormalizeStatus(string flutterwaveStatus)
    {
        return flutterwaveStatus.ToLowerInvariant() switch
        {
            "successful" => "COMPLETED",
            "success" => "COMPLETED",
            "completed" => "COMPLETED",
            "pending" => "PENDING",
            "processing" => "PROCESSING",
            "failed" => "FAILED",
            "cancelled" => "CANCELLED",
            _ => "PENDING"
        };
    }

    private static string? ExtractCountryCode(string? paymentType)
    {
        // Infer country from payment type (e.g., "mobile_money_uganda" -> "UG")
        if (string.IsNullOrEmpty(paymentType))
            return null;

        return paymentType.ToLower() switch
        {
            var type when type.Contains("nigeria") => "NG",
            var type when type.Contains("kenya") => "KE",
            var type when type.Contains("ghana") => "GH",
            var type when type.Contains("uganda") => "UG",
            var type when type.Contains("south_africa") => "ZA",
            _ => "NG" // Default to Nigeria
        };
    }
}

#region Flutterwave Models

internal class FlutterwaveWebhookPayload
{
    public string? Event { get; set; }
    public FlutterwaveWebhookData? Data { get; set; }
}

internal class FlutterwaveWebhookData
{
    public int? Id { get; set; }
    public string? TxRef { get; set; }
    public string? FlwRef { get; set; }
    public string? Currency { get; set; }
    public string? DestinationCurrency { get; set; }
    public decimal? Amount { get; set; }
    public decimal? AmountSettled { get; set; }
    public string? Status { get; set; }
    public string? PaymentType { get; set; }
    public string? Narration { get; set; }
    public DateTime? CreatedAt { get; set; }
    public FlutterwaveCustomer? Customer { get; set; }
}

internal class FlutterwaveCustomer
{
    public int? Id { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Name { get; set; }
    public string? Country { get; set; }
}

internal class FlutterwaveTransferResponse
{
    public string? Status { get; set; }
    public string? Message { get; set; }
    public FlutterwaveRateData? Data { get; set; }
}

internal class FlutterwaveRateData
{
    public decimal Rate { get; set; }
    public string? Source { get; set; }
    public string? Destination { get; set; }
}

#endregion
