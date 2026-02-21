namespace PayGuardAI.Core.Entities;

/// <summary>
/// Per-user notification preferences within a tenant.
/// Controls which email notification types are sent and to whom.
/// </summary>
public class NotificationPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    // Notification type toggles
    public bool RiskAlertsEnabled { get; set; } = true;
    public bool DailySummaryEnabled { get; set; } = true;
    public bool ReviewAssignmentsEnabled { get; set; } = true;
    public bool TeamInvitesEnabled { get; set; } = true;
    public bool SystemAlertsEnabled { get; set; } = true;

    // Threshold — only alert if risk score >= this value
    public int MinimumRiskScoreForAlert { get; set; } = 50;

    // Digest preference: "instant", "hourly", "daily"
    public string DigestFrequency { get; set; } = "instant";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
