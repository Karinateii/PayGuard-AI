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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string UpdatedBy { get; set; } = "system";
}
