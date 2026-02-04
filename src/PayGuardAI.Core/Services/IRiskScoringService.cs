using PayGuardAI.Core.Entities;

namespace PayGuardAI.Core.Services;

/// <summary>
/// Service for analyzing transaction risk.
/// </summary>
public interface IRiskScoringService
{
    /// <summary>
    /// Analyze a transaction and generate risk assessment.
    /// </summary>
    Task<RiskAnalysis> AnalyzeTransactionAsync(Transaction transaction, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Recalculate risk for an existing transaction (after rule changes).
    /// </summary>
    Task<RiskAnalysis> ReanalyzeTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default);
}
