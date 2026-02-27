namespace PayGuardAI.Core.Entities;

/// <summary>
/// Configurable risk rule for the scoring engine.
/// Supports two modes:
///   1. Built-in rules — RuleCode matches a hardcoded evaluation method (HIGH_AMOUNT, etc.)
///   2. Expression rules — ExpressionField/Operator/Value define a dynamic condition
/// </summary>
public class RiskRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Tenant this rule belongs to. Empty string means global/shared rule.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;
    
    /// <summary>
    /// Unique rule identifier (e.g., "HIGH_AMOUNT", "VELOCITY_24H", "EXPR_xxxx").
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
    /// For expression rules this may mirror ExpressionValue for numeric fields.
    /// </summary>
    public decimal Threshold { get; set; }
    
    /// <summary>
    /// Points to add to risk score when triggered.
    /// </summary>
    public int ScoreWeight { get; set; }
    
    /// <summary>
    /// Rule mode: "Active" (scores transactions), "Shadow" (evaluates but doesn't score),
    /// or "Disabled" (not evaluated). Replaces the old IsEnabled boolean.
    /// </summary>
    public string Mode { get; set; } = "Active";

    /// <summary>
    /// Backward-compatible convenience property.
    /// A rule is "enabled" if its mode is Active or Shadow (it gets evaluated either way).
    /// </summary>
    public bool IsEnabled
    {
        get => Mode != "Disabled";
        set => Mode = value ? "Active" : "Disabled";
    }

    /// <summary>
    /// True when the rule evaluates but its score is logged only — not added to the total.
    /// </summary>
    public bool IsShadow => Mode == "Shadow";
    
    // ── Expression Engine fields ─────────────────────────────────────
    // When these are populated, the scoring engine evaluates the rule
    // dynamically instead of requiring a hardcoded switch case.

    /// <summary>
    /// The transaction/profile field to evaluate.
    /// e.g., "Amount", "SourceCountry", "TotalTransactions", "TransactionHour".
    /// Null means this is a built-in rule (evaluated via RuleCode switch).
    /// </summary>
    public string? ExpressionField { get; set; }

    /// <summary>
    /// Comparison operator: ">=", "<=", ">", "&lt;", "==", "!=", "contains", "not_contains".
    /// </summary>
    public string? ExpressionOperator { get; set; }

    /// <summary>
    /// The value to compare against (stored as string, parsed at evaluation time).
    /// </summary>
    public string? ExpressionValue { get; set; }

    /// <summary>
    /// True if this is a built-in system rule (cannot be deleted, only configured).
    /// </summary>
    public bool IsBuiltIn => string.IsNullOrEmpty(ExpressionField) && BuiltInRuleCodes.Contains(RuleCode);

    /// <summary>
    /// True if this is a dynamic expression rule (user-created).
    /// </summary>
    public bool IsExpression => !string.IsNullOrEmpty(ExpressionField);

    /// <summary>
    /// When the rule was last modified.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Who last modified the rule.
    /// </summary>
    public string UpdatedBy { get; set; } = "system";

    /// <summary>
    /// The 6 built-in rule codes that have hardcoded evaluation logic.
    /// </summary>
    public static readonly HashSet<string> BuiltInRuleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "HIGH_AMOUNT",
        "VELOCITY_24H",
        "NEW_CUSTOMER",
        "HIGH_RISK_CORRIDOR",
        "ROUND_AMOUNT",
        "UNUSUAL_TIME"
    };

    /// <summary>
    /// Available fields for expression rules with display names and types.
    /// </summary>
    public static readonly Dictionary<string, (string DisplayName, string FieldType, string Hint)> ExpressionFields = new()
    {
        ["Amount"]              = ("Transaction Amount",       "decimal", "e.g., 5000"),
        ["SourceCountry"]       = ("Source Country",           "string",  "ISO code, e.g., NG"),
        ["DestinationCountry"]  = ("Destination Country",     "string",  "ISO code, e.g., US"),
        ["SourceCurrency"]      = ("Source Currency",          "string",  "e.g., USD"),
        ["DestinationCurrency"] = ("Destination Currency",    "string",  "e.g., NGN"),
        ["TransactionHour"]     = ("Transaction Hour (UTC)",  "int",     "0–23"),
        ["TotalTransactions"]   = ("Customer Total Txns",     "int",     "e.g., 10"),
        ["TotalVolume"]         = ("Customer Total Volume",   "decimal", "e.g., 50000"),
        ["AvgTransaction"]      = ("Customer Avg Txn Amount", "decimal", "e.g., 2000"),
        ["MaxTransaction"]      = ("Customer Max Txn Amount", "decimal", "e.g., 10000"),
        ["FlaggedCount"]        = ("Customer Flagged Count",  "int",     "e.g., 3"),
    };

    /// <summary>
    /// Available operators grouped by field type.
    /// </summary>
    public static readonly Dictionary<string, string> NumericOperators = new()
    {
        [">="] = "is greater than or equal to",
        ["<="] = "is less than or equal to",
        [">"]  = "is greater than",
        ["<"]  = "is less than",
        ["=="] = "equals",
        ["!="] = "does not equal",
    };

    public static readonly Dictionary<string, string> StringOperators = new()
    {
        ["=="]           = "equals",
        ["!="]           = "does not equal",
        ["contains"]     = "contains",
        ["not_contains"] = "does not contain",
    };
}
