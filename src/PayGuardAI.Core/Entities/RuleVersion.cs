namespace PayGuardAI.Core.Entities;

/// <summary>
/// Immutable snapshot of a rule's configuration taken before every edit.
/// Supports one-click rollback for both RiskRules and RuleGroups.
///
/// EntityType: "RiskRule" or "RuleGroup"
/// EntityId: The Guid PK of the rule that was snapshotted
/// ConfigJson: Full JSON serialization of the rule at that point in time
/// </summary>
public class RuleVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Tenant this version belongs to.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>"RiskRule" or "RuleGroup"</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>PK of the rule that was snapshotted.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Auto-incrementing version number per rule (1, 2, 3, ...).</summary>
    public int VersionNumber { get; set; }

    /// <summary>Full JSON snapshot of the rule config at this version.</summary>
    public string ConfigJson { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable summary of what changed.
    /// e.g., "Threshold: 5000 → 10000, Weight: 15 → 20"
    /// </summary>
    public string ChangeDescription { get; set; } = string.Empty;

    /// <summary>Who made the change.</summary>
    public string ChangedBy { get; set; } = "system";

    /// <summary>When the snapshot was taken.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Optional: the name of the rule at time of snapshot (for display).</summary>
    public string RuleName { get; set; } = string.Empty;
}
