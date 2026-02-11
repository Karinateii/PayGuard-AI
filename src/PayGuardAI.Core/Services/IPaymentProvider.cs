namespace PayGuardAI.Core.Services;

/// <summary>
/// Abstraction for payment provider integrations (Afriex, Flutterwave, Wise, etc.)
/// Normalizes webhooks and API operations across multiple payment platforms
/// </summary>
public interface IPaymentProvider
{
    /// <summary>
    /// Unique provider identifier (e.g., "afriex", "flutterwave", "wise")
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Normalize incoming webhook payload to unified transaction format
    /// </summary>
    /// <param name="webhookPayload">Raw webhook JSON</param>
    /// <returns>Normalized transaction data</returns>
    Task<NormalizedTransaction> NormalizeWebhookAsync(object webhookPayload);

    /// <summary>
    /// Verify webhook signature to ensure authenticity
    /// </summary>
    /// <param name="payload">Raw webhook body</param>
    /// <param name="signature">Signature from webhook header</param>
    /// <returns>True if signature is valid</returns>
    bool VerifyWebhookSignature(string payload, string signature);

    /// <summary>
    /// Get exchange rate between two currencies
    /// </summary>
    Task<decimal> GetExchangeRateAsync(string fromCurrency, string toCurrency, decimal amount);

    /// <summary>
    /// Check if provider is properly configured and ready
    /// </summary>
    bool IsConfigured();
}

/// <summary>
/// Unified transaction format across all payment providers
/// </summary>
public class NormalizedTransaction
{
    public required string TransactionId { get; set; }
    public required string Provider { get; set; }
    public required string CustomerId { get; set; }
    public string? CustomerEmail { get; set; }
    public required string SourceCurrency { get; set; }
    public required decimal SourceAmount { get; set; }
    public required string DestinationCurrency { get; set; }
    public required decimal DestinationAmount { get; set; }
    public required string SourceCountry { get; set; }
    public required string DestinationCountry { get; set; }
    public required string Status { get; set; }
    public required DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Factory for creating payment provider instances based on configuration
/// </summary>
public interface IPaymentProviderFactory
{
    /// <summary>
    /// Get the appropriate provider based on feature flags and configuration
    /// </summary>
    IPaymentProvider GetProvider(string? providerHint = null);

    /// <summary>
    /// Get provider by name (e.g., "afriex", "flutterwave")
    /// </summary>
    IPaymentProvider GetProviderByName(string providerName);

    /// <summary>
    /// Get all available configured providers
    /// </summary>
    IEnumerable<IPaymentProvider> GetAllProviders();
}
