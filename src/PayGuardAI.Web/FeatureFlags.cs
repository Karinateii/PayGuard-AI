using Microsoft.Extensions.Configuration;

namespace PayGuardAI.Web;

/// <summary>
/// Feature flag constants - use this to access feature flags consistently across the app
/// </summary>
public static class FeatureFlags
{
    public const string SectionName = "FeatureFlags";
    
    public const string PostgresEnabledKey = "PostgresEnabled";
    public const string OAuthEnabledKey = "OAuthEnabled";
    public const string FlutterwaveEnabledKey = "FlutterwaveEnabled";
    public const string SlackAlertsEnabledKey = "SlackAlertsEnabled";
    public const string BillingEnabledKey = "BillingEnabled";
    public const string FlutterwaveBillingEnabledKey = "FlutterwaveBillingEnabled";
    public const string EmailNotificationsEnabledKey = "EmailNotificationsEnabled";
    public const string WiseEnabledKey = "WiseEnabled";

    public static bool IsPostgresEnabled(this IConfiguration config)
    {
        var section = config.GetSection(SectionName);
        var value = section[PostgresEnabledKey];
        return bool.TryParse(value, out var result) && result;
    }

    public static bool IsOAuthEnabled(this IConfiguration config)
    {
        var section = config.GetSection(SectionName);
        var value = section[OAuthEnabledKey];
        return bool.TryParse(value, out var result) && result;
    }

    public static bool IsFlutterwaveEnabled(this IConfiguration config)
    {
        var section = config.GetSection(SectionName);
        var value = section[FlutterwaveEnabledKey];
        return bool.TryParse(value, out var result) && result;
    }

    public static bool IsSlackAlertsEnabled(this IConfiguration config)
    {
        var section = config.GetSection(SectionName);
        var value = section[SlackAlertsEnabledKey];
        return bool.TryParse(value, out var result) && result;
    }

    public static bool IsBillingEnabled(this IConfiguration config)
    {
        var section = config.GetSection(SectionName);
        var value = section[BillingEnabledKey];
        return bool.TryParse(value, out var result) && result;
    }

    public static bool IsFlutterwaveBillingEnabled(this IConfiguration config)
    {
        var section = config.GetSection(SectionName);
        var value = section[FlutterwaveBillingEnabledKey];
        return bool.TryParse(value, out var result) && result;
    }

    public static bool IsEmailNotificationsEnabled(this IConfiguration config)
    {
        var section = config.GetSection(SectionName);
        var value = section[EmailNotificationsEnabledKey];
        return bool.TryParse(value, out var result) && result;
    }

    public static bool IsWiseEnabled(this IConfiguration config)
    {
        var section = config.GetSection(SectionName);
        var value = section[WiseEnabledKey];
        return bool.TryParse(value, out var result) && result;
    }
}
