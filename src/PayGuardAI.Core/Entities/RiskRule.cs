namespace PayGuardAI.Core.Entities;

/// <summary>
/// Configurable risk rule for the scoring engine.
/// Allows compliance officers to tune thresholds.
/// </summary>
public class RiskRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Tenant this rule belongs to. Empty string means global/shared rule.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;
    
    /// <summary>
    /// Unique rule identifier (e.g., "HIGH_AMOUNT", "VELOCITY_24H").
    /// </summary>
    public string RuleCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Human-readable rule name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Description of what this rule checks.
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Category for grouping (Amount, Velocity, Geography, Pattern).
    /// </summary>
    public string Category { get; set; } = string.Empty;
    
    /// <summary>
    /// Threshold value (interpretation depends on rule type).
    /// </summary>
    public decimal Threshold { get; set; }
    
    /// <summary>
    /// Points to add to risk score when triggered.
    /// </summary>
    public int ScoreWeight { get; set; }
    
    /// <summary>
    /// Is this rule currently active?
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// When the rule was last modified.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Who last modified the rule.
    /// </summary>
    public string UpdatedBy { get; set; } = "system";
}
