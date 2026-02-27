using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Calculates how much money PayGuard AI has saved the tenant by blocking
/// or flagging fraudulent transactions that were subsequently rejected.
///
/// "Saved" = Amount of transactions whose review status is Rejected.
/// These are transactions the scoring engine flagged and a human reviewer
/// (or auto-reject policy) confirmed as fraudulent / unwanted.
/// </summary>
public class FraudSavingsService : IFraudSavingsService
{
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<FraudSavingsService> _logger;

    private const string SummaryCacheKey = "fraud-savings-summary";
    private const string ByRuleCacheKey = "fraud-savings-by-rule";

    public FraudSavingsService(
        ApplicationDbContext context,
        IMemoryCache cache,
        ITenantContext tenantContext,
        ILogger<FraudSavingsService> logger)
    {
        _context = context;
        _cache = cache;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<FraudSavingsSummary> GetSavingsSummaryAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{_tenantContext.TenantId}:{SummaryCacheKey}";
        if (_cache.TryGetValue(cacheKey, out FraudSavingsSummary? cached) && cached != null)
        {
            return cached;
        }

        var now = DateTime.UtcNow;
        var thisMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var lastMonthStart = thisMonthStart.AddMonths(-1);
        var thisWeekStart = now.Date.AddDays(-(int)now.DayOfWeek);

        // Base query: rejected transactions joined with their amounts
        // A transaction is "saved" when it was flagged by our rules and then rejected.
        var rejectedQuery = _context.RiskAnalyses
            .Include(ra => ra.Transaction)
            .Where(ra => ra.ReviewStatus == ReviewStatus.Rejected);

        // ── This month ──
        var thisMonthData = await rejectedQuery
            .Where(ra => ra.ReviewedAt >= thisMonthStart)
            .Select(ra => new { ra.Transaction.Amount })
            .ToListAsync(cancellationToken);

        var savedThisMonth = thisMonthData.Sum(x => x.Amount);
        var blockedThisMonth = thisMonthData.Count;

        // ── Last month ──
        var savedLastMonth = await rejectedQuery
            .Where(ra => ra.ReviewedAt >= lastMonthStart && ra.ReviewedAt < thisMonthStart)
            .SumAsync(ra => ra.Transaction.Amount, cancellationToken);

        // ── This week ──
        var savedThisWeek = await rejectedQuery
            .Where(ra => ra.ReviewedAt >= thisWeekStart)
            .SumAsync(ra => ra.Transaction.Amount, cancellationToken);

        // ── All time ──
        var allTimeData = await rejectedQuery
            .Select(ra => new { ra.Transaction.Amount })
            .ToListAsync(cancellationToken);

        var savedAllTime = allTimeData.Sum(x => x.Amount);
        var blockedAllTime = allTimeData.Count;

        // ── Trend ──
        var trendPercent = savedLastMonth > 0
            ? (double)((savedThisMonth - savedLastMonth) / savedLastMonth * 100)
            : (savedThisMonth > 0 ? 100.0 : 0.0);

        // ── Most common currency ──
        var currencyCode = await _context.Transactions
            .GroupBy(t => t.SourceCurrency)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefaultAsync(cancellationToken) ?? "USD";

        var summary = new FraudSavingsSummary
        {
            SavedThisMonth = savedThisMonth,
            SavedLastMonth = savedLastMonth,
            SavedThisWeek = savedThisWeek,
            SavedAllTime = savedAllTime,
            BlockedCountThisMonth = blockedThisMonth,
            BlockedCountAllTime = blockedAllTime,
            TrendPercent = Math.Round(trendPercent, 1),
            CurrencyCode = currencyCode
        };

        _cache.Set(cacheKey, summary, TimeSpan.FromSeconds(30));

        _logger.LogInformation(
            "Fraud savings for {TenantId}: ${SavedMonth:N0} this month, ${SavedAll:N0} all-time ({BlockedAll} blocked)",
            _tenantContext.TenantId, savedThisMonth, savedAllTime, blockedAllTime);

        return summary;
    }

    public async Task<List<RuleSavingsInfo>> GetSavingsByRuleAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{_tenantContext.TenantId}:{ByRuleCacheKey}";
        if (_cache.TryGetValue(cacheKey, out List<RuleSavingsInfo>? cached) && cached != null)
        {
            return cached;
        }

        // Find all risk factors that contributed to rejected transactions.
        // Each factor's parent RiskAnalysis links to a Transaction with an Amount.
        var rejectedFactors = await _context.RiskFactors
            .Include(rf => rf.RiskAnalysis)
                .ThenInclude(ra => ra.Transaction)
            .Where(rf => rf.RiskAnalysis.ReviewStatus == ReviewStatus.Rejected && !rf.IsShadow)
            .ToListAsync(cancellationToken);

        // Group by rule name, sum the transaction amounts (each factor shares credit)
        var grouped = rejectedFactors
            .GroupBy(rf => new { rf.RuleName, rf.Category })
            .Select(g =>
            {
                // Distinct transactions this rule contributed to
                var distinctTxIds = g.Select(rf => rf.RiskAnalysis.TransactionId).Distinct().ToList();
                var totalAmount = g
                    .GroupBy(rf => rf.RiskAnalysis.TransactionId)
                    .Sum(txGroup => txGroup.First().RiskAnalysis.Transaction.Amount);

                return new RuleSavingsInfo
                {
                    RuleName = g.Key.RuleName,
                    Category = g.Key.Category,
                    AmountSaved = totalAmount,
                    TransactionCount = distinctTxIds.Count
                };
            })
            .OrderByDescending(r => r.AmountSaved)
            .ToList();

        _cache.Set(cacheKey, grouped, TimeSpan.FromSeconds(30));
        return grouped;
    }
}
