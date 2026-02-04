using PayGuardAI.Core.Entities;

namespace PayGuardAI.Core.Services;

/// <summary>
/// Service for managing transactions.
/// </summary>
public interface ITransactionService
{
    /// <summary>
    /// Process an incoming webhook and create a transaction.
    /// </summary>
    Task<Transaction> ProcessWebhookAsync(string payload, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get transactions with optional filtering.
    /// </summary>
    Task<IEnumerable<Transaction>> GetTransactionsAsync(
        int? pageNumber = null,
        int? pageSize = null,
        RiskLevel? riskLevel = null,
        ReviewStatus? reviewStatus = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get a transaction by ID.
    /// </summary>
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get dashboard statistics.
    /// </summary>
    Task<DashboardStats> GetDashboardStatsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Dashboard statistics DTO.
/// </summary>
public class DashboardStats
{
    public int TotalTransactions { get; set; }
    public int PendingReviews { get; set; }
    public int HighRiskCount { get; set; }
    public int ApprovedToday { get; set; }
    public int RejectedToday { get; set; }
    public decimal TotalVolumeToday { get; set; }
    public double AverageRiskScore { get; set; }
}
