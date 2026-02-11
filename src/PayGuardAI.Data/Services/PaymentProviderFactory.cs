using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Factory for creating and managing payment provider instances
/// Selects provider based on feature flags and configuration
/// </summary>
public class PaymentProviderFactory : IPaymentProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PaymentProviderFactory> _logger;
    private readonly Dictionary<string, IPaymentProvider> _providers;

    public PaymentProviderFactory(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<PaymentProviderFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
        _providers = new Dictionary<string, IPaymentProvider>(StringComparer.OrdinalIgnoreCase);

        InitializeProviders();
    }

    private void InitializeProviders()
    {
        // Always register Afriex (primary provider)
        var afriexProvider = _serviceProvider.GetService(typeof(AfriexProvider)) as IPaymentProvider;
        if (afriexProvider != null)
        {
            _providers[afriexProvider.ProviderName] = afriexProvider;
            _logger.LogInformation("Registered payment provider: {Provider}", afriexProvider.ProviderName);
        }

        // Conditionally register Flutterwave based on feature flag
        var flutterwaveSection = _configuration.GetSection("FeatureFlags");
        var flutterwaveEnabled = bool.TryParse(flutterwaveSection["FlutterwaveEnabled"], out var enabled) && enabled;
        
        if (flutterwaveEnabled)
        {
            var flutterwaveProvider = _serviceProvider.GetService(typeof(FlutterwaveProvider)) as IPaymentProvider;
            if (flutterwaveProvider != null && flutterwaveProvider.IsConfigured())
            {
                _providers[flutterwaveProvider.ProviderName] = flutterwaveProvider;
                _logger.LogInformation("Registered payment provider: {Provider}", flutterwaveProvider.ProviderName);
            }
            else
            {
                _logger.LogWarning("Flutterwave enabled but not properly configured");
            }
        }
    }

    public IPaymentProvider GetProvider(string? providerHint = null)
    {
        // If specific provider requested, try to get it
        if (!string.IsNullOrEmpty(providerHint) && _providers.TryGetValue(providerHint, out var hintedProvider))
        {
            _logger.LogDebug("Using provider from hint: {Provider}", providerHint);
            return hintedProvider;
        }

        // Default priority: Flutterwave (if enabled) > Afriex
        var flutterwaveSection = _configuration.GetSection("FeatureFlags");
        var flutterwaveEnabled = bool.TryParse(flutterwaveSection["FlutterwaveEnabled"], out var enabled) && enabled;
        
        if (flutterwaveEnabled && _providers.ContainsKey("flutterwave"))
        {
            _logger.LogDebug("Using default provider: flutterwave");
            return _providers["flutterwave"];
        }

        if (_providers.ContainsKey("afriex"))
        {
            _logger.LogDebug("Using default provider: afriex");
            return _providers["afriex"];
        }

        throw new InvalidOperationException("No payment providers are configured");
    }

    public IPaymentProvider GetProviderByName(string providerName)
    {
        if (_providers.TryGetValue(providerName, out var provider))
        {
            return provider;
        }

        throw new ArgumentException($"Payment provider '{providerName}' is not registered or not configured", nameof(providerName));
    }

    public IEnumerable<IPaymentProvider> GetAllProviders()
    {
        return _providers.Values;
    }
}
