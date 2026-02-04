using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Transaction service implementation.
/// Handles webhook processing and transaction queries.
/// </summary>
public class TransactionService : ITransactionService
{
    private readonly ApplicationDbContext _context;
    private readonly IRiskScoringService _riskScoringService;
    private readonly ILogger<TransactionService> _logger;

    public TransactionService(
        ApplicationDbContext context,
        IRiskScoringService riskScoringService,
        ILogger<TransactionService> logger)
    {
        _context = context;
        _riskScoringService = riskScoringService;
        _logger = logger;
    }

    public async Task<Transaction> ProcessWebhookAsync(string payload, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing incoming webhook");

        // Parse the webhook payload
        var transaction = ParseWebhookPayload(payload);
        
        // Check for duplicate
        var existing = await _context.Transactions
            .FirstOrDefaultAsync(t => t.ExternalId == transaction.ExternalId, cancellationToken);

        if (existing != null)
        {
            _logger.LogWarning("Duplicate transaction received: {ExternalId}", transaction.ExternalId);
            return existing;
        }

        // Save transaction
        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Transaction {TransactionId} saved, starting risk analysis", transaction.Id);

        // Perform risk analysis
        await _riskScoringService.AnalyzeTransactionAsync(transaction, cancellationToken);

        return transaction;
    }

    public async Task<IEnumerable<Transaction>> GetTransactionsAsync(
        int? pageNumber = null,
        int? pageSize = null,
        RiskLevel? riskLevel = null,
        ReviewStatus? reviewStatus = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Transactions
            .Include(t => t.RiskAnalysis)
                .ThenInclude(r => r!.RiskFactors)
            .AsQueryable();

        if (riskLevel.HasValue)
        {
            query = query.Where(t => t.RiskAnalysis != null && t.RiskAnalysis.RiskLevel == riskLevel.Value);
        }

        if (reviewStatus.HasValue)
        {
            query = query.Where(t => t.RiskAnalysis != null && t.RiskAnalysis.ReviewStatus == reviewStatus.Value);
        }

        query = query.OrderByDescending(t => t.ReceivedAt);

        if (pageNumber.HasValue && pageSize.HasValue)
        {
            query = query
                .Skip((pageNumber.Value - 1) * pageSize.Value)
                .Take(pageSize.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .Include(t => t.RiskAnalysis)
                .ThenInclude(r => r!.RiskFactors)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<DashboardStats> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        var totalTransactions = await _context.Transactions.CountAsync(cancellationToken);
        
        var pendingReviews = await _context.RiskAnalyses
            .CountAsync(r => r.ReviewStatus == ReviewStatus.Pending || r.ReviewStatus == ReviewStatus.Escalated, 
                cancellationToken);

        var highRiskCount = await _context.RiskAnalyses
            .CountAsync(r => r.RiskLevel >= RiskLevel.High && 
                           (r.ReviewStatus == ReviewStatus.Pending || r.ReviewStatus == ReviewStatus.Escalated), 
                cancellationToken);

        var approvedToday = await _context.RiskAnalyses
            .CountAsync(r => r.ReviewedAt >= today && r.ReviewedAt < tomorrow && 
                           r.ReviewStatus == ReviewStatus.Approved, 
                cancellationToken);

        var rejectedToday = await _context.RiskAnalyses
            .CountAsync(r => r.ReviewedAt >= today && r.ReviewedAt < tomorrow && 
                           r.ReviewStatus == ReviewStatus.Rejected, 
                cancellationToken);

        var totalVolumeToday = await _context.Transactions
            .Where(t => t.ReceivedAt >= today && t.ReceivedAt < tomorrow)
            .SumAsync(t => t.Amount, cancellationToken);

        var averageRiskScore = await _context.RiskAnalyses
            .Where(r => r.AnalyzedAt >= today && r.AnalyzedAt < tomorrow)
            .AverageAsync(r => (double?)r.RiskScore, cancellationToken) ?? 0;

        return new DashboardStats
        {
            TotalTransactions = totalTransactions,
            PendingReviews = pendingReviews,
            HighRiskCount = highRiskCount,
            ApprovedToday = approvedToday,
            RejectedToday = rejectedToday,
            TotalVolumeToday = totalVolumeToday,
            AverageRiskScore = averageRiskScore
        };
    }

    private Transaction ParseWebhookPayload(string payload)
    {
        // Parse the Afriex webhook payload format
        // Based on the API documentation
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var transaction = new Transaction
        {
            RawPayload = payload,
            ReceivedAt = DateTime.UtcNow
        };

        // Extract data from webhook - adapt to actual Afriex format
        if (root.TryGetProperty("data", out var data))
        {
            transaction.ExternalId = data.TryGetProperty("id", out var id) 
                ? id.GetString() ?? Guid.NewGuid().ToString() 
                : Guid.NewGuid().ToString();

            transaction.Type = data.TryGetProperty("type", out var type) 
                ? type.GetString() ?? "UNKNOWN" 
                : "UNKNOWN";

            transaction.Status = data.TryGetProperty("status", out var status) 
                ? status.GetString() ?? "UNKNOWN" 
                : "UNKNOWN";

            transaction.Amount = data.TryGetProperty("amount", out var amount) 
                ? amount.GetDecimal() 
                : 0;

            transaction.SourceCurrency = data.TryGetProperty("sourceCurrency", out var srcCur) 
                ? srcCur.GetString() ?? "USD" 
                : "USD";

            transaction.DestinationCurrency = data.TryGetProperty("destinationCurrency", out var destCur) 
                ? destCur.GetString() ?? "USD" 
                : "USD";

            transaction.SenderId = data.TryGetProperty("senderId", out var sender) 
                ? sender.GetString() ?? "unknown" 
                : "unknown";

            transaction.ReceiverId = data.TryGetProperty("receiverId", out var receiver) 
                ? receiver.GetString() 
                : null;

            transaction.SourceCountry = data.TryGetProperty("sourceCountry", out var srcCountry) 
                ? srcCountry.GetString() ?? "US" 
                : "US";

            transaction.DestinationCountry = data.TryGetProperty("destinationCountry", out var destCountry) 
                ? destCountry.GetString() ?? "NG" 
                : "NG";

            if (data.TryGetProperty("createdAt", out var createdAt) && 
                DateTime.TryParse(createdAt.GetString(), out var parsedDate))
            {
                transaction.CreatedAt = parsedDate;
            }
            else
            {
                transaction.CreatedAt = DateTime.UtcNow;
            }
        }
        else
        {
            // Handle simple/flat format (direct fields, not wrapped in "data")
            transaction.ExternalId = root.TryGetProperty("transactionId", out var txnId) 
                ? txnId.GetString() ?? Guid.NewGuid().ToString()
                : root.TryGetProperty("id", out var id) 
                    ? id.GetString() ?? Guid.NewGuid().ToString() 
                    : Guid.NewGuid().ToString();

            transaction.Type = root.TryGetProperty("type", out var type) 
                ? type.GetString() ?? "remittance" 
                : "remittance";

            transaction.Status = root.TryGetProperty("status", out var status) 
                ? status.GetString() ?? "pending" 
                : "pending";

            transaction.Amount = root.TryGetProperty("amount", out var amount) 
                ? amount.GetDecimal() 
                : 0;

            transaction.SourceCurrency = root.TryGetProperty("sourceCurrency", out var srcCur) 
                ? srcCur.GetString() ?? "USD" 
                : "USD";

            transaction.DestinationCurrency = root.TryGetProperty("destinationCurrency", out var destCur) 
                ? destCur.GetString() ?? "NGN" 
                : "NGN";

            transaction.SenderId = root.TryGetProperty("senderId", out var sender) 
                ? sender.GetString() ?? "unknown" 
                : "unknown";

            transaction.ReceiverId = root.TryGetProperty("receiverId", out var receiver) 
                ? receiver.GetString() 
                : null;

            transaction.SourceCountry = root.TryGetProperty("sourceCountry", out var srcCountry) 
                ? srcCountry.GetString() ?? "US" 
                : "US";

            transaction.DestinationCountry = root.TryGetProperty("destinationCountry", out var destCountry) 
                ? destCountry.GetString() ?? "NG" 
                : "NG";

            transaction.CreatedAt = DateTime.UtcNow;
        }

        return transaction;
    }
}
