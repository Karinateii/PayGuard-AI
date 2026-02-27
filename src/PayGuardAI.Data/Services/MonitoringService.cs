using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Provides real-time operational monitoring metrics for the platform.
/// Powers the Monitoring Dashboard with system health, throughput, error rates,
/// latency percentiles, and trend data.
///
/// Uses IDbContextFactory for Blazor Server concurrency safety.
/// Caches results for 15 seconds to avoid hammering the database.
/// </summary>
public class MonitoringService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MonitoringService> _logger;

    public MonitoringService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        ITenantContext tenantContext,
        IMemoryCache cache,
        ILogger<MonitoringService> logger)
    {
        _dbFactory = dbFactory;
        _tenantContext = tenantContext;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Get comprehensive monitoring snapshot for the current tenant.
    /// </summary>
    public async Task<MonitoringSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var cacheKey = $"{_tenantContext.TenantId}:monitoring-snapshot";
        if (_cache.TryGetValue(cacheKey, out MonitoringSnapshot? cached) && cached != null)
            return cached;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenantId(_tenantContext.TenantId);

        var now = DateTime.UtcNow;
        var last24h = now.AddHours(-24);
        var last1h = now.AddHours(-1);
        var last7d = now.AddDays(-7);

        // ── Transaction throughput ──
        var txnLast24h = await db.Transactions
            .Where(t => t.CreatedAt >= last24h)
            .CountAsync(ct);

        var txnLastHour = await db.Transactions
            .Where(t => t.CreatedAt >= last1h)
            .CountAsync(ct);

        var txnLast7d = await db.Transactions
            .Where(t => t.CreatedAt >= last7d)
            .CountAsync(ct);

        var totalTransactions = await db.Transactions.CountAsync(ct);

        // ── Risk breakdown (last 24h) ──
        var riskBreakdown = await db.RiskAnalyses
            .Where(ra => ra.AnalyzedAt >= last24h)
            .GroupBy(ra => ra.RiskLevel)
            .Select(g => new { Level = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var highRisk24h = riskBreakdown.Where(r => r.Level == RiskLevel.High || r.Level == RiskLevel.Critical).Sum(r => r.Count);
        var mediumRisk24h = riskBreakdown.Where(r => r.Level == RiskLevel.Medium).Sum(r => r.Count);
        var lowRisk24h = riskBreakdown.Where(r => r.Level == RiskLevel.Low).Sum(r => r.Count);

        // ── Review queue ──
        var pendingReviews = await db.RiskAnalyses
            .Where(ra => ra.ReviewStatus == ReviewStatus.Pending)
            .CountAsync(ct);

        var reviewedLast24h = await db.RiskAnalyses
            .Where(ra => ra.ReviewedAt >= last24h && ra.ReviewStatus != ReviewStatus.Pending)
            .CountAsync(ct);

        // ── Average risk score (last 24h) ──
        var avgRiskScore = await db.RiskAnalyses
            .Where(ra => ra.AnalyzedAt >= last24h)
            .Select(ra => (double?)ra.RiskScore)
            .AverageAsync(ct) ?? 0;

        // ── Hourly throughput (last 24 hours for chart) ──
        var hourlyThroughput = await db.Transactions
            .Where(t => t.CreatedAt >= last24h)
            .GroupBy(t => new { t.CreatedAt.Year, t.CreatedAt.Month, t.CreatedAt.Day, t.CreatedAt.Hour })
            .Select(g => new HourlyMetric
            {
                Hour = new DateTime(g.Key.Year, g.Key.Month, g.Key.Day, g.Key.Hour, 0, 0, DateTimeKind.Utc),
                Count = g.Count(),
                HighRiskCount = 0 // Will be filled separately
            })
            .OrderBy(h => h.Hour)
            .ToListAsync(ct);

        // Fill high-risk counts per hour
        var hourlyHighRisk = await db.RiskAnalyses
            .Where(ra => ra.AnalyzedAt >= last24h && (ra.RiskLevel == RiskLevel.High || ra.RiskLevel == RiskLevel.Critical))
            .GroupBy(ra => new { ra.AnalyzedAt.Year, ra.AnalyzedAt.Month, ra.AnalyzedAt.Day, ra.AnalyzedAt.Hour })
            .Select(g => new { Hour = new DateTime(g.Key.Year, g.Key.Month, g.Key.Day, g.Key.Hour, 0, 0, DateTimeKind.Utc), Count = g.Count() })
            .ToListAsync(ct);

        foreach (var hr in hourlyHighRisk)
        {
            var match = hourlyThroughput.FirstOrDefault(h => h.Hour == hr.Hour);
            if (match != null) match.HighRiskCount = hr.Count;
        }

        // ── Error rate from system logs (last 24h) ──
        var totalLogs24h = await db.SystemLogs
            .IgnoreQueryFilters()
            .Where(l => l.CreatedAt >= last24h)
            .CountAsync(ct);

        var errorLogs24h = await db.SystemLogs
            .IgnoreQueryFilters()
            .Where(l => l.CreatedAt >= last24h && (l.Level == "Error" || l.Level == "Fatal"))
            .CountAsync(ct);

        var warningLogs24h = await db.SystemLogs
            .IgnoreQueryFilters()
            .Where(l => l.CreatedAt >= last24h && l.Level == "Warning")
            .CountAsync(ct);

        var errorRate = totalLogs24h > 0 ? (double)errorLogs24h / totalLogs24h * 100 : 0;

        // ── Recent errors (last 5) ──
        var recentErrors = await db.SystemLogs
            .IgnoreQueryFilters()
            .Where(l => l.Level == "Error" || l.Level == "Fatal")
            .OrderByDescending(l => l.CreatedAt)
            .Take(5)
            .Select(l => new RecentError
            {
                Message = l.Message,
                Source = l.SourceContext ?? "Unknown",
                OccurredAt = l.CreatedAt,
                Level = l.Level
            })
            .ToListAsync(ct);

        // ── Daily transaction trend (last 7 days) ──
        var dailyTrend = await db.Transactions
            .Where(t => t.CreatedAt >= last7d)
            .GroupBy(t => t.CreatedAt.Date)
            .Select(g => new DailyTrend
            {
                Date = g.Key,
                TransactionCount = g.Count(),
                Volume = g.Sum(t => t.Amount)
            })
            .OrderBy(d => d.Date)
            .ToListAsync(ct);

        // ── Active rules count ──
        var activeRules = await db.RiskRules
            .Where(r => r.IsEnabled)
            .CountAsync(ct);

        // ── Webhook activity (last 24h) ──
        var webhookEvents24h = await db.AuditLogs
            .Where(a => a.CreatedAt >= last24h && a.Action.Contains("webhook"))
            .CountAsync(ct);

        // ── Uptime estimate (based on error gaps in last 7 days) ──
        var criticalErrors7d = await db.SystemLogs
            .IgnoreQueryFilters()
            .Where(l => l.CreatedAt >= last7d && (l.Level == "Fatal" || l.Level == "Error"))
            .CountAsync(ct);

        // Simple uptime heuristic: 100% - (critical errors / expected checks)
        // In production this would come from an uptime monitor
        var uptimePercent = Math.Max(95.0, 100.0 - (criticalErrors7d * 0.01));

        var snapshot = new MonitoringSnapshot
        {
            GeneratedAt = now,
            TenantId = _tenantContext.TenantId,

            // Throughput
            TransactionsLast24h = txnLast24h,
            TransactionsLastHour = txnLastHour,
            TransactionsLast7d = txnLast7d,
            TotalTransactions = totalTransactions,
            ThroughputPerMinute = txnLastHour / 60.0,

            // Risk
            HighRiskLast24h = highRisk24h,
            MediumRiskLast24h = mediumRisk24h,
            LowRiskLast24h = lowRisk24h,
            AverageRiskScore = Math.Round(avgRiskScore, 1),

            // Reviews
            PendingReviews = pendingReviews,
            ReviewedLast24h = reviewedLast24h,

            // Errors
            ErrorsLast24h = errorLogs24h,
            WarningsLast24h = warningLogs24h,
            ErrorRate = Math.Round(errorRate, 2),
            RecentErrors = recentErrors,

            // System
            ActiveRules = activeRules,
            WebhookEventsLast24h = webhookEvents24h,
            UptimePercent = Math.Round(uptimePercent, 2),

            // Charts
            HourlyThroughput = hourlyThroughput,
            DailyTrend = dailyTrend
        };

        _cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(15));
        _logger.LogDebug("Monitoring snapshot generated for tenant {TenantId}: {Txn24h} txns/24h, {ErrorRate}% error rate",
            _tenantContext.TenantId, txnLast24h, errorRate);

        return snapshot;
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────

/// <summary>
/// Complete monitoring snapshot for the dashboard.
/// </summary>
public class MonitoringSnapshot
{
    public DateTime GeneratedAt { get; set; }
    public string TenantId { get; set; } = string.Empty;

    // Throughput
    public int TransactionsLast24h { get; set; }
    public int TransactionsLastHour { get; set; }
    public int TransactionsLast7d { get; set; }
    public int TotalTransactions { get; set; }
    public double ThroughputPerMinute { get; set; }

    // Risk
    public int HighRiskLast24h { get; set; }
    public int MediumRiskLast24h { get; set; }
    public int LowRiskLast24h { get; set; }
    public double AverageRiskScore { get; set; }

    // Reviews
    public int PendingReviews { get; set; }
    public int ReviewedLast24h { get; set; }

    // Errors
    public int ErrorsLast24h { get; set; }
    public int WarningsLast24h { get; set; }
    public double ErrorRate { get; set; }
    public List<RecentError> RecentErrors { get; set; } = [];

    // System
    public int ActiveRules { get; set; }
    public int WebhookEventsLast24h { get; set; }
    public double UptimePercent { get; set; }

    // Charts
    public List<HourlyMetric> HourlyThroughput { get; set; } = [];
    public List<DailyTrend> DailyTrend { get; set; } = [];

    // Health status helpers
    public string HealthStatus => ErrorRate switch
    {
        > 10 => "Degraded",
        > 5 => "Warning",
        _ => "Healthy"
    };

    public string HealthColor => ErrorRate switch
    {
        > 10 => "#f44336",
        > 5 => "#ff9800",
        _ => "#4caf50"
    };
}

public class HourlyMetric
{
    public DateTime Hour { get; set; }
    public int Count { get; set; }
    public int HighRiskCount { get; set; }
}

public class DailyTrend
{
    public DateTime Date { get; set; }
    public int TransactionCount { get; set; }
    public decimal Volume { get; set; }
}

public class RecentError
{
    public string Message { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public string Level { get; set; } = string.Empty;
}
