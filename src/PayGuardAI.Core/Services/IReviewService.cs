using PayGuardAI.Core.Entities;

namespace PayGuardAI.Core.Services;

/// <summary>
/// Service for Human-in-the-Loop review workflow.
/// </summary>
public interface IReviewService
{
    /// <summary>
    /// Approve a transaction after review.
    /// </summary>
    Task<RiskAnalysis> ApproveAsync(Guid analysisId, string reviewedBy, string? notes = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Reject a transaction after review.
    /// </summary>
    Task<RiskAnalysis> RejectAsync(Guid analysisId, string reviewedBy, string? notes = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Escalate a transaction for senior review.
    /// </summary>
    Task<RiskAnalysis> EscalateAsync(Guid analysisId, string reviewedBy, string? notes = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get pending reviews with optional filtering.
    /// </summary>
    Task<IEnumerable<RiskAnalysis>> GetPendingReviewsAsync(
        RiskLevel? minRiskLevel = null,
        int? limit = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get review history for audit purposes.
    /// </summary>
    Task<IEnumerable<RiskAnalysis>> GetReviewHistoryAsync(
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? reviewedBy = null,
        CancellationToken cancellationToken = default);
}
