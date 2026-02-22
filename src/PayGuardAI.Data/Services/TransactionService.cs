using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
    private readonly IBillingService _billingService;
    private readonly ILogger<TransactionService> _logger;
    private readonly IMemoryCache _cache;
    private readonly ITenantContext _tenantContext;
    private readonly IMetricsService _metrics;

    private const string DashboardCacheKey = "dashboard-stats";
    private const string TransactionsCacheKey = "transactions";

    public TransactionService(
        ApplicationDbContext context,
        IRiskScoringService riskScoringService,
        IBillingService billingService,
        ILogger<TransactionService> logger,
        IMemoryCache cache,
        ITenantContext tenantContext,
        IMetricsService metrics)
    {
        _context = context;
        _riskScoringService = riskScoringService;
        _billingService = billingService;
        _logger = logger;
        _cache = cache;
        _tenantContext = tenantContext;
        _metrics = metrics;
    }

    public async Task<Transaction> ProcessWebhookAsync(string payload, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing incoming webhook");

        // Parse the webhook payload
        var transaction = ParseWebhookPayload(payload);
        transaction.TenantId = _tenantContext.TenantId;
        
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
        var riskAnalysis = await _riskScoringService.AnalyzeTransactionAsync(transaction, cancellationToken);

        // Record Prometheus metrics
        var outcome = riskAnalysis.ReviewStatus == ReviewStatus.AutoApproved ? "auto_approved" : "flagged";
        _metrics.RecordTransactionProcessed(riskAnalysis.RiskLevel.ToString().ToLower(), outcome);
        _metrics.RecordRiskScore(riskAnalysis.RiskScore);

        // Record billing usage (increments TransactionsThisPeriod counter)
        try
        {
            var tenantId = _tenantContext.TenantId;
            await _billingService.RecordTransactionUsageAsync(tenantId, cancellationToken);
        }
        catch (Exception ex)
        {
            // Billing tracking should never block transaction processing
            _logger.LogWarning(ex, "Failed to record billing usage for transaction {TransactionId}", transaction.Id);
        }

        _logger.LogInformation(
            "Transaction {TransactionId} risk analysis complete: Score={RiskScore}, Level={RiskLevel}, Outcome={Outcome}",
            transaction.Id, riskAnalysis.RiskScore, riskAnalysis.RiskLevel, outcome);

        InvalidateCaches();

        return transaction;
    }

    public async Task<IEnumerable<Transaction>> GetTransactionsAsync(
        int? pageNumber = null,
        int? pageSize = null,
        RiskLevel? riskLevel = null,
        ReviewStatus? reviewStatus = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey($"{TransactionsCacheKey}:{pageNumber}:{pageSize}:{riskLevel}:{reviewStatus}");
        if (_cache.TryGetValue(cacheKey, out IEnumerable<Transaction> cached))
        {
            return cached;
        }

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

        var result = await query.ToListAsync(cancellationToken);
        _cache.Set(cacheKey, result, TimeSpan.FromSeconds(15));
        return result;
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
        var cacheKey = GetCacheKey(DashboardCacheKey);
        if (_cache.TryGetValue(cacheKey, out DashboardStats cached))
        {
            return cached;
        }

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

        var stats = new DashboardStats
        {
            TotalTransactions = totalTransactions,
            PendingReviews = pendingReviews,
            HighRiskCount = highRiskCount,
            ApprovedToday = approvedToday,
            RejectedToday = rejectedToday,
            TotalVolumeToday = totalVolumeToday,
            AverageRiskScore = averageRiskScore
        };

        _cache.Set(cacheKey, stats, TimeSpan.FromSeconds(10));
        return stats;
    }

    private void InvalidateCaches()
    {
        _cache.Remove(GetCacheKey(DashboardCacheKey));
    }

    private string GetCacheKey(string key) => $"{_tenantContext.TenantId}:{key}";

    private Transaction ParseWebhookPayload(string payload)
    {
        // Parse the Afriex webhook payload format
        // Supports both Afriex event format and direct transaction format
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var transaction = new Transaction
        {
            RawPayload = payload,
            ReceivedAt = DateTime.UtcNow
        };

        // Check for Afriex event format: { "event": "TRANSACTION.CREATED", "data": {...} }
        JsonElement data;
        string eventType = "";
        
        if (root.TryGetProperty("event", out var eventProp))
        {
            eventType = eventProp.GetString() ?? "";
            _logger.LogInformation("Processing Afriex event: {EventType}", eventType);
            
            if (!root.TryGetProperty("data", out data))
            {
                _logger.LogWarning("Afriex event has no data property");
                data = root;
            }
        }
        else if (root.TryGetProperty("data", out data))
        {
            // Wrapped format without event field
            _logger.LogInformation("Processing wrapped webhook format");
        }
        else
        {
            // Direct/flat format
            data = root;
            _logger.LogInformation("Processing flat webhook format");
        }

        // Parse transaction from data element
        // Afriex uses transactionId, customerId, etc.
        transaction.ExternalId = GetStringValue(data, "transactionId", "id") ?? Guid.NewGuid().ToString();
        transaction.Type = GetStringValue(data, "type") ?? "WITHDRAWAL";
        transaction.Status = GetStringValue(data, "status") ?? "PENDING";
        
        // Amount handling - Afriex sends amounts as strings
        var sourceAmount = GetStringValue(data, "sourceAmount", "amount");
        transaction.Amount = decimal.TryParse(sourceAmount, out var amt) ? amt : 0;
        
        // If destinationAmount is present, might want to use that for display
        var destAmount = GetStringValue(data, "destinationAmount");
        if (destAmount != null && decimal.TryParse(destAmount, out var destAmt) && destAmt > 0)
        {
            // Store the larger amount for risk assessment
            transaction.Amount = Math.Max(transaction.Amount, destAmt);
        }
        
        transaction.SourceCurrency = GetStringValue(data, "sourceCurrency") ?? "USD";
        transaction.DestinationCurrency = GetStringValue(data, "destinationCurrency") ?? "NGN";
        
        // Afriex uses customerId instead of senderId
        transaction.SenderId = GetStringValue(data, "customerId", "senderId") ?? "unknown";
        transaction.ReceiverId = GetStringValue(data, "destinationId", "receiverId");
        
        // Country codes from currencies if not specified
        transaction.SourceCountry = GetStringValue(data, "sourceCountry") ?? GetCountryFromCurrency(transaction.SourceCurrency);
        transaction.DestinationCountry = GetStringValue(data, "destinationCountry") ?? GetCountryFromCurrency(transaction.DestinationCurrency);
        
        // Parse dates
        var createdAtStr = GetStringValue(data, "createdAt");
        if (!string.IsNullOrEmpty(createdAtStr) && DateTime.TryParse(createdAtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedDate))
        {
            transaction.CreatedAt = parsedDate.Kind == DateTimeKind.Utc ? parsedDate : parsedDate.ToUniversalTime();
        }
        else
        {
            transaction.CreatedAt = DateTime.UtcNow;
        }

        return transaction;
    }
    
    private static string? GetStringValue(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                return prop.ValueKind switch
                {
                    JsonValueKind.String => prop.GetString(),
                    JsonValueKind.Number => prop.GetRawText(),
                    _ => null
                };
            }
        }
        return null;
    }
    
    private static string GetCountryFromCurrency(string currency)
    {
        return currency.ToUpperInvariant() switch
        {
            "USD" => "US",
            "NGN" => "NG",
            "GHS" => "GH",
            "KES" => "KE",
            "ZAR" => "ZA",
            "GBP" => "GB",
            "EUR" => "EU",
            "CAD" => "CA",
            "TZS" => "TZ",
            "UGX" => "UG",
            "RWF" => "RW",
            "XOF" => "SN", // West African CFA
            "XAF" => "CM", // Central African CFA
            _ => "XX"
        };
    }
}
