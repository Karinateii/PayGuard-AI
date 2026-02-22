namespace PayGuardAI.Core.Entities;

/// <summary>
/// Risk analysis result for a transaction.
/// </summary>
public class RiskAnalysis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Tenant this risk analysis belongs to.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;
    
    public Guid TransactionId { get; set; }
    
    /// <summary>
    /// Risk score from 0 to 100. Higher = more risky.
    /// </summary>
    public int RiskScore { get; set; }
    
    /// <summary>
    /// Risk level classification.
    /// </summary>
    public RiskLevel RiskLevel { get; set; }
    
    /// <summary>
    /// Current review status.
    /// </summary>
    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Pending;
    
    /// <summary>
    /// Human-readable explanation of the risk assessment (XAI).
    /// </summary>
    public string Explanation { get; set; } = string.Empty;
    
    /// <summary>
    /// When the analysis was performed.
    /// </summary>
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Who reviewed this (null if auto-approved or pending).
    /// </summary>
    public string? ReviewedBy { get; set; }
    
    /// <summary>
    /// When the review was completed.
    /// </summary>
    public DateTime? ReviewedAt { get; set; }
    
    /// <summary>
    /// Reviewer's notes/comments.
    /// </summary>
    public string? ReviewNotes { get; set; }
    
    // Navigation properties
    public Transaction Transaction { get; set; } = null!;
    public List<RiskFactor> RiskFactors { get; set; } = new();
}

/// <summary>
/// Risk level classification.
/// </summary>
public enum RiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// Review status for Human-in-the-Loop workflow.
/// </summary>
public enum ReviewStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Escalated = 3,
    AutoApproved = 4
}
