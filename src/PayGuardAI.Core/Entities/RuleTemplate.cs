namespace PayGuardAI.Core.Entities;

/// <summary>
/// A pre-configured risk rule template available in the Rule Marketplace.
/// Templates are global (not tenant-scoped) — every tenant can browse and import them.
///
/// When imported, a template creates or updates a tenant-scoped <see cref="RiskRule"/>
/// with the template's recommended threshold and weight values.
///
/// RuleCode must match one of the evaluation methods in RiskScoringService:
///   HIGH_AMOUNT, VELOCITY_24H, NEW_CUSTOMER, HIGH_RISK_CORRIDOR, ROUND_AMOUNT, UNUSUAL_TIME
/// </summary>
public class RuleTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Human-readable template name (e.g., "Remittance: Large Transfer Alert").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description of what this template optimizes for and why.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The scoring engine rule code this template configures.
    /// Must match a case in RiskScoringService.EvaluateRuleAsync:
    /// HIGH_AMOUNT | VELOCITY_24H | NEW_CUSTOMER | HIGH_RISK_CORRIDOR | ROUND_AMOUNT | UNUSUAL_TIME
    /// </summary>
    public string RuleCode { get; set; } = string.Empty;

    /// <summary>
    /// Rule category for grouping: Amount, Velocity, Geography, or Pattern.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Recommended threshold value (interpretation depends on RuleCode).
    /// </summary>
    public decimal Threshold { get; set; }

    /// <summary>
    /// Recommended score weight (points added when triggered, 0–50).
    /// </summary>
    public int ScoreWeight { get; set; }

    /// <summary>
    /// Target industry: Remittance, E-Commerce, Lending, Crypto, or General.
    /// Used for filtering and "Import Industry Pack" feature.
    /// </summary>
    public string Industry { get; set; } = "General";

    /// <summary>
    /// Free-text tags for search/filter (stored as comma-separated string in DB).
    /// </summary>
    public string[] Tags { get; set; } = [];

    /// <summary>
    /// True for system-provided templates. False for community-contributed (future).
    /// </summary>
    public bool IsBuiltIn { get; set; } = true;

    /// <summary>
    /// Number of times this template has been imported across all tenants.
    /// Used as a popularity signal.
    /// </summary>
    public int ImportCount { get; set; }

    /// <summary>
    /// Who created this template ("PayGuard AI" for built-in).
    /// </summary>
    public string Author { get; set; } = "PayGuard AI";

    /// <summary>
    /// Template version string for tracking updates.
    /// </summary>
    public string Version { get; set; } = "1.0";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
