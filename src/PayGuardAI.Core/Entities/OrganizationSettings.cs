namespace PayGuardAI.Core.Entities;

/// <summary>
/// Organization / tenant settings — name, logo, timezone, etc.
/// </summary>
public class OrganizationSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = "My Organization";
    public string? LogoUrl { get; set; }
    public string Timezone { get; set; } = "UTC";
    public string DefaultCurrency { get; set; } = "USD";
    public string? SupportEmail { get; set; }
    public string? WebhookUrl { get; set; }
    public int AutoApproveThreshold { get; set; } = 20;
    public int AutoRejectThreshold { get; set; } = 80;
    /// <summary>
    /// Comma-separated list of high-risk country codes (ISO 3166-1 alpha-2)
    /// used by the HIGH_RISK_CORRIDOR rule. Defaults to OFAC-sanctioned countries.
    /// Admins can customise per tenant from Organization Settings.
    /// </summary>
    public string HighRiskCountries { get; set; } = "IR,KP,SY,YE,VE,CU,MM,AF";
    /// <summary>
    /// Comma-separated list of allowed IP addresses for API access.
    /// Empty = all IPs allowed.
    /// </summary>
    public string? IpWhitelist { get; set; }
    /// <summary>
    /// Slack incoming-webhook URL for this tenant's alerts.
    /// If empty, falls back to the global Slack:WebhookUrl config.
    /// </summary>
    public string? SlackWebhookUrl { get; set; }
    /// <summary>
    /// Set to true when the tenant completes the onboarding wizard.
    /// Used to redirect first-time users from the dashboard to onboarding.
    /// </summary>
    public bool OnboardingCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string UpdatedBy { get; set; } = "system";

    /// <summary>
    /// Default OFAC/FATF high-risk country codes used when no tenant override exists.
    /// </summary>
    public static readonly HashSet<string> DefaultHighRiskCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        "IR", "KP", "SY", "YE", "VE", "CU", "MM", "AF"
    };

    /// <summary>
    /// Parse the comma-separated <see cref="HighRiskCountries"/> string into a HashSet.
    /// Falls back to <see cref="DefaultHighRiskCountries"/> if the string is empty.
    /// </summary>
    public HashSet<string> GetHighRiskCountrySet()
    {
        if (string.IsNullOrWhiteSpace(HighRiskCountries))
            return DefaultHighRiskCountries;

        return new HashSet<string>(
            HighRiskCountries.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
    }
}
