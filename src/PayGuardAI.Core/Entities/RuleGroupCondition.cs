namespace PayGuardAI.Core.Entities;

/// <summary>
/// A single condition within a compound rule (RuleGroup).
/// Uses the same expression fields and operators as single expression rules.
/// </summary>
public class RuleGroupCondition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// FK to the parent RuleGroup.
    /// </summary>
    public Guid RuleGroupId { get; set; }

    /// <summary>
    /// The transaction/profile field to evaluate.
    /// Uses the same field names as RiskRule.ExpressionFields:
    /// Amount, SourceCountry, DestinationCountry, SourceCurrency, DestinationCurrency,
    /// TransactionHour, TotalTransactions, TotalVolume, AvgTransaction, MaxTransaction, FlaggedCount.
    /// </summary>
    public string ExpressionField { get; set; } = string.Empty;

    /// <summary>
    /// Comparison operator: ">=", "<=", ">", "&lt;", "==", "!=", "contains", "not_contains".
    /// </summary>
    public string ExpressionOperator { get; set; } = string.Empty;

    /// <summary>
    /// The value to compare against (stored as string, parsed at evaluation time).
    /// </summary>
    public string ExpressionValue { get; set; } = string.Empty;

    /// <summary>
    /// Display ordering within the group (0-based).
    /// </summary>
    public int OrderIndex { get; set; }

    /// <summary>
    /// Navigation property to parent group.
    /// </summary>
    public RuleGroup? RuleGroup { get; set; }
}
