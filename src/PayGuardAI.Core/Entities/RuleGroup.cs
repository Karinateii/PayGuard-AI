namespace PayGuardAI.Core.Entities;

/// <summary>
/// A compound rule that combines multiple conditions with AND/OR logic.
/// When triggered, adds RiskPoints to the transaction's risk score.
/// 
/// Example: "Amount > 5000 AND SourceCountry == NG AND TotalTransactions < 3"
/// → This would flag large transfers to Nigeria from new customers.
/// </summary>
public class RuleGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Tenant this compound rule belongs to.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name for the compound rule.
    /// e.g., "High-Value New Customer from Nigeria"
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description explaining the business rationale.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// How to combine the conditions: "AND" (all must match) or "OR" (any must match).
    /// </summary>
    public string LogicalOperator { get; set; } = "AND";

    /// <summary>
    /// Risk points to add when this compound rule triggers.
    /// </summary>
    public int RiskPoints { get; set; } = 20;

    /// <summary>
    /// Category for grouping (Amount, Velocity, Geography, Pattern, Compound).
    /// </summary>
    public string Category { get; set; } = "Compound";

    /// <summary>
    /// Is this compound rule currently active?
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// When the rule was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the rule was last modified.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who created/last modified this rule.
    /// </summary>
    public string CreatedBy { get; set; } = "system";

    /// <summary>
    /// Navigation property — the individual conditions in this compound rule.
    /// </summary>
    public ICollection<RuleGroupCondition> Conditions { get; set; } = new List<RuleGroupCondition>();
}
