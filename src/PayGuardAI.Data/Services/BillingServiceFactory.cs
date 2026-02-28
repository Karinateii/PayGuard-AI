using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Resolves the correct IBillingService implementation based on the selected provider.
/// Supports Paystack (Nigerian/African customers) and Flutterwave (international customers).
///
/// Provider selection:
/// 1. Explicit provider parameter (from pricing page when user picks)
/// 2. Config default: FeatureFlags:FlutterwaveBillingEnabled → FlutterwaveBillingService
///    Otherwise → PaystackBillingService
///
/// Both providers share the same IBillingService interface and TenantSubscription entity.
/// </summary>
public class BillingServiceFactory
{
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _config;

    public BillingServiceFactory(IServiceProvider sp, IConfiguration config)
    {
        _sp = sp;
        _config = config;
    }

    /// <summary>
    /// Get the default billing service based on feature flags and configuration.
    /// Falls back to Paystack if Flutterwave is not fully configured.
    /// </summary>
    public IBillingService GetDefault()
    {
        if (IsFlutterwaveBillingAvailable())
            return GetFlutterwave();

        return GetPaystack();
    }

    /// <summary>
    /// Get a billing service for a specific provider.
    /// </summary>
    public IBillingService GetProvider(string provider) => provider.ToLowerInvariant() switch
    {
        "flutterwave" => GetFlutterwave(),
        "paystack" => GetPaystack(),
        _ => GetDefault()
    };

    /// <summary>
    /// Get the Paystack billing service.
    /// </summary>
    public IBillingService GetPaystack()
        => _sp.GetRequiredKeyedService<IBillingService>("paystack");

    /// <summary>
    /// Get the Flutterwave billing service.
    /// </summary>
    public IBillingService GetFlutterwave()
        => _sp.GetRequiredKeyedService<IBillingService>("flutterwave");

    /// <summary>
    /// Check if Flutterwave billing is available (configured + enabled).
    /// </summary>
    public bool IsFlutterwaveBillingAvailable()
        => IsFlutterwaveBillingEnabled() && !string.IsNullOrWhiteSpace(_config["FlutterwaveBilling:SecretKey"]);

    /// <summary>
    /// Check if Paystack billing is available (configured + enabled).
    /// </summary>
    public bool IsPaystackBillingAvailable()
        => !string.IsNullOrWhiteSpace(_config["Paystack:SecretKey"]);

    /// <summary>
    /// Check if both providers are available (dual-provider mode).
    /// </summary>
    public bool IsDualProviderAvailable()
        => IsPaystackBillingAvailable() && IsFlutterwaveBillingAvailable();

    private bool IsFlutterwaveBillingEnabled()
    {
        var value = _config.GetSection("FeatureFlags")["FlutterwaveBillingEnabled"];
        return bool.TryParse(value, out var result) && result;
    }
}
