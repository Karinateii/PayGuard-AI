namespace PayGuardAI.Core.Entities;

/// <summary>
/// Individual risk factor that contributed to the overall risk score.
/// Provides explainability (XAI) for the risk assessment.
/// </summary>
public class RiskFactor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Tenant this risk factor belongs to.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;
    
    public Guid RiskAnalysisId { get; set; }
    
    /// <summary>
    /// Factor category (e.g., "Amount", "Velocity", "Geography", "Pattern").
    /// </summary>
    public string Category { get; set; } = string.Empty;
    
    /// <summary>
    /// Specific rule or check that triggered.
    /// </summary>
    public string RuleName { get; set; } = string.Empty;
    
    /// <summary>
    /// Human-readable description of why this is a risk factor.
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Points contributed to the total risk score.
    /// </summary>
    public int ScoreContribution { get; set; }
    
    /// <summary>
    /// Severity of this individual factor.
    /// </summary>
    public FactorSeverity Severity { get; set; }
    
    /// <summary>
    /// Additional context data (JSON format).
    /// </summary>
    public string? ContextData { get; set; }
    
    // Navigation
    public RiskAnalysis RiskAnalysis { get; set; } = null!;
}

/// <summary>
/// Severity of a risk factor.
/// </summary>
public enum FactorSeverity
{
    Info = 0,
    Warning = 1,
    Alert = 2,
    Critical = 3
}
