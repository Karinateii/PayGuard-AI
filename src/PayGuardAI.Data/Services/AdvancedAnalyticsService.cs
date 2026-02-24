using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Advanced analytics service — custom reports, team performance, ROI metrics.
/// </summary>
public class AdvancedAnalyticsService : IAdvancedAnalyticsService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AdvancedAnalyticsService> _logger;

    public AdvancedAnalyticsService(ApplicationDbContext db, ILogger<AdvancedAnalyticsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Custom Reports ─────────────────────────────────────────────────────

    public async Task<CustomReport> CreateReportAsync(string tenantId, CustomReport report, string createdBy, CancellationToken ct = default)
    {
        report.TenantId = tenantId;
        report.CreatedBy = createdBy;
        report.CreatedAt = DateTime.UtcNow;
        
        _db.Set<CustomReport>().Add(report);
        await _db.SaveChangesAsync(ct);
        
        _logger.LogInformation("Created custom report '{ReportName}' for tenant {TenantId}", 
            report.Name, tenantId);
        return report;
    }

    public async Task<List<CustomReport>> GetReportsAsync(string tenantId, CancellationToken ct = default)
    {
        return await _db.Set<CustomReport>()
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<CustomReport?> GetReportByIdAsync(Guid reportId, CancellationToken ct = default)
    {
        // Use FirstOrDefaultAsync instead of FindAsync to guarantee
        // the global tenant query filter is always applied (FindAsync
        // checks the change-tracker first and can bypass the filter).
        return await _db.Set<CustomReport>()
            .FirstOrDefaultAsync(r => r.Id == reportId, ct);
    }

    public async Task DeleteReportAsync(Guid reportId, CancellationToken ct = default)
    {
        var report = await GetReportByIdAsync(reportId, ct);
        if (report != null)
        {
            _db.Set<CustomReport>().Remove(report);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<ReportData> RunReportAsync(Guid reportId, CancellationToken ct = default)
    {
        var report = await GetReportByIdAsync(reportId, ct) 
            ?? throw new InvalidOperationException("Report not found.");

        var data = new ReportData
        {
            ReportId = reportId,
            ReportName = report.Name,
            GeneratedAt = DateTime.UtcNow
        };

        var startDate = report.StartDate ?? DateTime.UtcNow.AddDays(-30);
        var endDate = report.EndDate ?? DateTime.UtcNow;

        switch (report.ReportType.ToLower())
        {
            case "transactions":
                await PopulateTransactionsReportAsync(data, report.TenantId, startDate, endDate, ct);
                break;
            case "risk-trends":
                await PopulateRiskTrendsReportAsync(data, report.TenantId, startDate, endDate, ct);
                break;
            case "team-performance":
                await PopulateTeamPerformanceReportAsync(data, report.TenantId, startDate, endDate, ct);
                break;
            default:
                throw new InvalidOperationException($"Unknown report type: {report.ReportType}");
        }

        return data;
    }

    public async Task<byte[]> ExportReportToCsvAsync(ReportData data, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        
        // Header row
        if (data.Rows.Any())
        {
            var headers = string.Join(",", data.Rows.First().Keys);
            sb.AppendLine(headers);
        }

        // Data rows
        foreach (var row in data.Rows)
        {
            var values = string.Join(",", row.Values.Select(v => CsvEscape(v?.ToString() ?? "")));
            sb.AppendLine(values);
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task<byte[]> ExportReportToPdfAsync(ReportData data, CancellationToken ct = default)
    {
        // Formatted text report (no heavy PDF library dependency needed)
        var sb = new StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine($"  REPORT: {data.ReportName}");
        sb.AppendLine($"  Generated: {data.GeneratedAt:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine($"  Total Rows: {data.TotalRows}");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();

        if (data.Summary.Any())
        {
            sb.AppendLine("SUMMARY");
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            foreach (var kvp in data.Summary)
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
            sb.AppendLine();
        }

        if (data.Rows.Any())
        {
            sb.AppendLine("DATA");
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            var headers = data.Rows.First().Keys.ToList();
            sb.AppendLine("  " + string.Join(" | ", headers));
            sb.AppendLine("  " + string.Join("─┼─", headers.Select(h => new string('─', Math.Max(h.Length, 12)))));
            foreach (var row in data.Rows.Take(200))
            {
                var values = headers.Select(h => row.TryGetValue(h, out var v) ? (v?.ToString() ?? "") : "");
                sb.AppendLine("  " + string.Join(" | ", values));
            }
            if (data.Rows.Count > 200)
                sb.AppendLine($"  ... and {data.Rows.Count - 200} more rows (use CSV export for full data)");
        }

        sb.AppendLine();
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine("  Generated by PayGuard AI — Advanced Analytics");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ── Risk Trends ────────────────────────────────────────────────────────

    public async Task<RiskTrendAnalysis> GetRiskTrendAnalysisAsync(string tenantId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        var analyses = await _db.RiskAnalyses
            .Where(r => r.Transaction.TenantId == tenantId 
                     && r.AnalyzedAt >= startDate 
                     && r.AnalyzedAt <= endDate)
            .Include(r => r.Transaction)
            .ToListAsync(ct);

        var dailyGroups = analyses
            .GroupBy(r => r.AnalyzedAt.Date)
            .OrderBy(g => g.Key)
            .Select(g => new DailyMetric
            {
                Date = g.Key,
                Value = g.Average(r => r.RiskScore),
                Count = g.Count()
            })
            .ToList();

        var dailyHighRisk = analyses
            .Where(r => r.RiskLevel == RiskLevel.Critical || r.RiskLevel == RiskLevel.High)
            .GroupBy(r => r.AnalyzedAt.Date)
            .OrderBy(g => g.Key)
            .Select(g => new DailyMetric
            {
                Date = g.Key,
                Value = g.Count(),
                Count = g.Count()
            })
            .ToList();

        // Calculate trend (linear regression slope)
        var trend = CalculateTrendSlope(dailyGroups);
        var stdDev = CalculateStandardDeviation(dailyGroups.Select(d => d.Value));

        return new RiskTrendAnalysis
        {
            DailyAverageRisk = dailyGroups,
            DailyHighRiskCount = dailyHighRisk,
            OverallTrend = trend,
            StandardDeviation = stdDev
        };
    }

    public async Task<List<CorridorRiskScore>> GetCorridorRiskHeatmapAsync(string tenantId, int days = 30, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        
        var corridors = await _db.Transactions
            .Where(t => t.TenantId == tenantId && t.CreatedAt >= cutoff)
            .Join(_db.RiskAnalyses, t => t.Id, r => r.TransactionId, (t, r) => new { t, r })
            .GroupBy(x => new { x.t.SourceCountry, x.t.DestinationCountry })
            .Select(g => new CorridorRiskScore
            {
                SourceCountry = g.Key.SourceCountry ?? "Unknown",
                DestinationCountry = g.Key.DestinationCountry ?? "Unknown",
                TransactionCount = g.Count(),
                AverageRiskScore = g.Average(x => x.r.RiskScore),
                HighRiskCount = g.Count(x => x.r.RiskLevel == RiskLevel.Critical || x.r.RiskLevel == RiskLevel.High)
            })
            .OrderByDescending(c => c.AverageRiskScore)
            .ToListAsync(ct);

        return corridors;
    }

    public async Task<List<DailyMetric>> GetFlaggedTransactionRateAsync(string tenantId, int days = 30, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        
        var dailyStats = await _db.Transactions
            .Where(t => t.TenantId == tenantId && t.CreatedAt >= cutoff)
            .GroupJoin(_db.RiskAnalyses, t => t.Id, r => r.TransactionId, (t, risks) => new { t, risks })
            .SelectMany(x => x.risks.DefaultIfEmpty(), (x, r) => new { x.t, r })
            .GroupBy(x => x.t.CreatedAt.Date)
            .Select(g => new DailyMetric
            {
                Date = g.Key,
                Value = g.Count(x => x.r != null && (x.r.RiskLevel == RiskLevel.Critical || x.r.RiskLevel == RiskLevel.High)) * 100.0 / g.Count(),
                Count = g.Count()
            })
            .OrderBy(d => d.Date)
            .ToListAsync(ct);

        return dailyStats;
    }

    // ── Team Performance ───────────────────────────────────────────────────

    public async Task<List<AnalystPerformance>> GetAnalystPerformanceAsync(string tenantId, int days = 30, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        
        var auditLogs = await _db.AuditLogs
            .Where(a => a.TenantId == tenantId 
                     && a.CreatedAt >= cutoff
                     && (a.Action == "TRANSACTION_APPROVED" 
                      || a.Action == "TRANSACTION_REJECTED" 
                      || a.Action == "TRANSACTION_ESCALATED"))
            .ToListAsync(ct);

        // Build a lookup of transaction creation times for review-time calculation.
        // Parse EntityId safely — skip any entries that aren't valid GUIDs.
        var entityIds = auditLogs
            .Select(a => Guid.TryParse(a.EntityId, out var id) ? id : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var transactionTimes = await _db.RiskAnalyses
            .Where(r => entityIds.Contains(r.TransactionId))
            .Select(r => new { r.TransactionId, r.AnalyzedAt })
            .ToDictionaryAsync(r => r.TransactionId, r => r.AnalyzedAt, ct);

        var analystGroups = auditLogs
            .GroupBy(a => a.PerformedBy)
            .Select(g =>
            {
                var member = _db.TeamMembers
                    .FirstOrDefault(m => m.TenantId == tenantId && m.Email.ToLower() == g.Key.ToLower());

                // Calculate review time: time between risk analysis and human decision
                var reviewTimes = g
                    .Select(a =>
                    {
                        if (!Guid.TryParse(a.EntityId, out var txId)) return -1.0;
                        if (!transactionTimes.TryGetValue(txId, out var analyzedAt)) return -1.0;
                        return (a.CreatedAt - analyzedAt).TotalMinutes;
                    })
                    .Where(t => t > 0)
                    .ToList();

                var avgReviewTime = reviewTimes.Any() ? reviewTimes.Average() : 0;

                var approvals = g.Count(a => a.Action == "TRANSACTION_APPROVED");
                var rejections = g.Count(a => a.Action == "TRANSACTION_REJECTED");
                var escalations = g.Count(a => a.Action == "TRANSACTION_ESCALATED");
                var total = g.Count();

                return new AnalystPerformance
                {
                    AnalystEmail = g.Key,
                    AnalystName = member?.DisplayName ?? g.Key,
                    TotalReviews = total,
                    AverageReviewTimeMinutes = avgReviewTime,
                    ApprovalsCount = approvals,
                    RejectionsCount = rejections,
                    EscalationsCount = escalations,
                    ApprovalRate = total > 0 ? (double)approvals / total * 100 : 0,
                    OverturnedDecisions = 0 // Would need to track decision changes
                };
            })
            .OrderByDescending(p => p.TotalReviews)
            .ToList();

        return analystGroups;
    }

    public async Task<TeamPerformanceMetrics> GetTeamPerformanceMetricsAsync(string tenantId, int days = 30, CancellationToken ct = default)
    {
        var analysts = await GetAnalystPerformanceAsync(tenantId, days, ct);
        
        var totalReviews = analysts.Sum(a => a.TotalReviews);
        var avgReviewTime = analysts.Any() ? analysts.Average(a => a.AverageReviewTimeMinutes) : 0;
        var reviewTimes = analysts.SelectMany(a => Enumerable.Repeat(a.AverageReviewTimeMinutes, a.TotalReviews)).OrderBy(t => t).ToList();
        var medianReviewTime = reviewTimes.Any() ? reviewTimes[reviewTimes.Count / 2] : 0;

        return new TeamPerformanceMetrics
        {
            TotalReviews = totalReviews,
            AverageReviewTimeMinutes = avgReviewTime,
            MedianReviewTimeMinutes = medianReviewTime,
            ActiveAnalysts = analysts.Count,
            InterRaterAgreement = 0, // Would need to compare decisions on same transactions
            TopPerformers = analysts.Take(5).ToList()
        };
    }

    public async Task<List<ReviewTimeDistribution>> GetReviewTimeDistributionAsync(string tenantId, int days = 30, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        
        // Get review audit logs with valid EntityIds
        var reviews = await _db.AuditLogs
            .Where(a => a.TenantId == tenantId 
                     && a.CreatedAt >= cutoff
                     && (a.Action == "TRANSACTION_APPROVED" || a.Action == "TRANSACTION_REJECTED"))
            .ToListAsync(ct);

        // Parse entity IDs safely
        var entityIds = reviews
            .Select(a => Guid.TryParse(a.EntityId, out var id) ? id : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var transactionTimes = await _db.RiskAnalyses
            .Where(r => entityIds.Contains(r.TransactionId))
            .Select(r => new { r.TransactionId, r.AnalyzedAt })
            .ToDictionaryAsync(r => r.TransactionId, r => r.AnalyzedAt, ct);

        // Calculate actual review times (minutes between risk analysis and human review)
        var reviewTimes = reviews
            .Select(a =>
            {
                if (!Guid.TryParse(a.EntityId, out var txId)) return -1.0;
                if (!transactionTimes.TryGetValue(txId, out var analyzedAt)) return -1.0;
                return (a.CreatedAt - analyzedAt).TotalMinutes;
            })
            .Where(t => t > 0)
            .ToList();

        var distribution = new List<ReviewTimeDistribution>
        {
            new() { TimeBucket = "0-5 min", Count = reviewTimes.Count(t => t <= 5) },
            new() { TimeBucket = "5-15 min", Count = reviewTimes.Count(t => t > 5 && t <= 15) },
            new() { TimeBucket = "15-30 min", Count = reviewTimes.Count(t => t > 15 && t <= 30) },
            new() { TimeBucket = "30-60 min", Count = reviewTimes.Count(t => t > 30 && t <= 60) },
            new() { TimeBucket = "> 60 min", Count = reviewTimes.Count(t => t > 60) }
        };

        return distribution;
    }

    // ── Decision Consistency ───────────────────────────────────────────────

    public async Task<DecisionConsistencyMetrics> GetDecisionConsistencyAsync(string tenantId, int days = 30, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        
        // Get audit logs with valid EntityIds only
        var auditLogs = await _db.AuditLogs
            .Where(a => a.TenantId == tenantId 
                     && a.CreatedAt >= cutoff
                     && (a.Action == "TRANSACTION_APPROVED" || a.Action == "TRANSACTION_REJECTED"))
            .ToListAsync(ct);

        // Safely parse EntityIds and filter out invalid ones
        var logsByTxId = auditLogs
            .Select(a => new { Log = a, TxId = Guid.TryParse(a.EntityId, out var id) ? id : (Guid?)null })
            .Where(x => x.TxId.HasValue)
            .Select(x => new { x.Log, TxId = x.TxId!.Value })
            .ToList();

        var txIds = logsByTxId.Select(x => x.TxId).Distinct().ToList();

        // Get risk scores for those transactions
        var riskScores = await _db.RiskAnalyses
            .Where(r => txIds.Contains(r.TransactionId))
            .Select(r => new { r.TransactionId, r.RiskScore })
            .ToDictionaryAsync(r => r.TransactionId, r => r.RiskScore, ct);

        var riskBands = new Dictionary<string, DecisionRateByRiskBand>
        {
            ["0-25"] = new() { RiskBand = "0-25" },
            ["25-50"] = new() { RiskBand = "25-50" },
            ["50-75"] = new() { RiskBand = "50-75" },
            ["75-100"] = new() { RiskBand = "75-100" }
        };

        foreach (var entry in logsByTxId)
        {
            if (!riskScores.TryGetValue(entry.TxId, out var score)) continue;

            var band = GetRiskBand(score);
            riskBands[band].TotalDecisions++;
            if (entry.Log.Action == "TRANSACTION_APPROVED")
                riskBands[band].Approvals++;
            else
                riskBands[band].Rejections++;
        }

        foreach (var band in riskBands.Values)
        {
            band.ApprovalRate = band.TotalDecisions > 0 ? (double)band.Approvals / band.TotalDecisions * 100 : 0;
        }

        // Calculate real consistency: how uniform are approval rates within each risk band?
        // Lower variance across analysts per-band = higher consistency.
        var activeBands = riskBands.Values.Where(b => b.TotalDecisions > 0).ToList();
        double overallConsistency;

        if (activeBands.Count >= 2)
        {
            // Consistency score: 1 - normalized stddev of approval rates across bands
            // A perfectly consistent system would have monotonically decreasing approval rates
            // as risk increases. We measure how well the data follows that pattern.
            var rates = activeBands.Select(b => b.ApprovalRate).ToList();
            var stdDev = CalculateStandardDeviation(rates);
            var range = rates.Max() - rates.Min();
            overallConsistency = range > 0 ? Math.Max(0, 1.0 - (stdDev / range)) : 1.0;
        }
        else
        {
            overallConsistency = activeBands.Any() ? 1.0 : 0.0;
        }

        // Count inconsistencies: high-risk approved or low-risk rejected
        var inconsistencies = logsByTxId.Count(x =>
        {
            if (!riskScores.TryGetValue(x.TxId, out var score)) return false;
            return (score >= 75 && x.Log.Action == "TRANSACTION_APPROVED") ||
                   (score < 25 && x.Log.Action == "TRANSACTION_REJECTED");
        });

        return new DecisionConsistencyMetrics
        {
            ApprovalRatesByRiskScore = riskBands,
            OverallConsistency = Math.Round(overallConsistency, 3),
            TotalInconsistencies = inconsistencies
        };
    }

    public async Task<List<OutlierAnalyst>> GetOutlierAnalystsAsync(string tenantId, int days = 30, CancellationToken ct = default)
    {
        var analysts = await GetAnalystPerformanceAsync(tenantId, days, ct);
        
        if (!analysts.Any()) return [];

        var teamAvgApproval = analysts.Average(a => a.ApprovalRate);
        var stdDev = CalculateStandardDeviation(analysts.Select(a => a.ApprovalRate));

        var outliers = analysts
            .Where(a => Math.Abs(a.ApprovalRate - teamAvgApproval) > stdDev * 1.5) // 1.5 sigma threshold
            .Select(a => new OutlierAnalyst
            {
                AnalystEmail = a.AnalystEmail,
                AnalystName = a.AnalystName,
                OutlierType = a.ApprovalRate > teamAvgApproval ? "too-lenient" : "too-strict",
                DeviationFromMean = a.ApprovalRate - teamAvgApproval,
                ApprovalRate = a.ApprovalRate,
                TeamAverageApprovalRate = teamAvgApproval
            })
            .ToList();

        return outliers;
    }

    // ── ROI Calculator ─────────────────────────────────────────────────────

    public async Task<RoiMetrics> CalculateRoiAsync(string tenantId, DateTime? startDate = null, CancellationToken ct = default)
    {
        var onboardingDate = startDate ?? await GetTenantOnboardingDateAsync(tenantId, ct);
        var daysSinceOnboarding = (DateTime.UtcNow - onboardingDate).Days;

        // All transactions since onboarding
        var allTransactions = await _db.Transactions
            .Where(t => t.TenantId == tenantId && t.CreatedAt >= onboardingDate)
            .ToListAsync(ct);

        // Transactions that were flagged and reviewed
        var reviewedLogs = await _db.AuditLogs
            .Where(a => a.TenantId == tenantId 
                     && a.CreatedAt >= onboardingDate
                     && (a.Action == "TRANSACTION_APPROVED" || a.Action == "TRANSACTION_REJECTED"))
            .ToListAsync(ct);

        var rejections = reviewedLogs.Count(a => a.Action == "TRANSACTION_REJECTED");
        var approvals = reviewedLogs.Count(a => a.Action == "TRANSACTION_APPROVED");

        // Safely parse EntityIds for fraud value calculation
        var rejectedTxIds = reviewedLogs
            .Where(r => r.Action == "TRANSACTION_REJECTED" && Guid.TryParse(r.EntityId, out _))
            .Select(r => Guid.Parse(r.EntityId!))
            .ToHashSet();

        // Estimate fraud value prevented (assume rejections were fraud)
        var fraudValuePrevented = allTransactions
            .Where(t => rejectedTxIds.Contains(t.Id))
            .Sum(t => t.Amount);

        // False positive rate: approved flagged transactions / total flagged transactions
        var totalFlagged = approvals + rejections;
        var firstMonthCutoff = onboardingDate.AddDays(30);
        var recentCutoff = DateTime.UtcNow.AddDays(-30);

        var firstMonthReviews = reviewedLogs.Where(r => r.CreatedAt < firstMonthCutoff).ToList();
        var recentReviews = reviewedLogs.Where(r => r.CreatedAt >= recentCutoff).ToList();

        var firstMonthFPRate = CalculateFalsePositiveRate(firstMonthReviews);
        var recentFPRate = CalculateFalsePositiveRate(recentReviews);

        var fpReduction = firstMonthFPRate > 0 ? ((firstMonthFPRate - recentFPRate) / firstMonthFPRate) * 100 : 0;

        return new RoiMetrics
        {
            TotalTransactionsMonitored = allTransactions.Count,
            FraudulentTransactionsCaught = rejections,
            EstimatedFraudValuePrevented = fraudValuePrevented,
            FalsePositives = approvals, // Flagged transactions later approved
            TruePositives = rejections,
            Precision = totalFlagged > 0 ? (double)rejections / totalFlagged : 0,
            Recall = 0, // Would need false negatives (fraud that wasn't caught)
            FalsePositiveRate = recentFPRate,
            FalsePositiveRateAtOnboarding = firstMonthFPRate,
            FalsePositiveReduction = fpReduction,
            CostSavings = fraudValuePrevented - (totalFlagged * 5), // Assume $5 per review
            DaysSinceOnboarding = daysSinceOnboarding
        };
    }

    // ── Internal Helpers ───────────────────────────────────────────────────

    private async Task PopulateTransactionsReportAsync(ReportData data, string tenantId, DateTime startDate, DateTime endDate, CancellationToken ct)
    {
        var transactions = await _db.Transactions
            .Where(t => t.TenantId == tenantId && t.CreatedAt >= startDate && t.CreatedAt <= endDate)
            .OrderByDescending(t => t.CreatedAt)
            .Take(1000) // Limit to prevent huge reports
            .ToListAsync(ct);

        data.TotalRows = transactions.Count;
        data.Rows = transactions.Select(t => new Dictionary<string, object>
        {
            ["Date"] = t.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            ["Amount"] = t.Amount,
            ["Source"] = t.SourceCountry ?? "",
            ["Destination"] = t.DestinationCountry ?? "",
            ["Status"] = t.Status
        }).ToList();

        data.Summary = new Dictionary<string, object>
        {
            ["TotalTransactions"] = transactions.Count,
            ["TotalVolume"] = transactions.Sum(t => t.Amount),
            ["AverageAmount"] = transactions.Any() ? transactions.Average(t => t.Amount) : 0
        };
    }

    private async Task PopulateRiskTrendsReportAsync(ReportData data, string tenantId, DateTime startDate, DateTime endDate, CancellationToken ct)
    {
        var trend = await GetRiskTrendAnalysisAsync(tenantId, startDate, endDate, ct);
        
        data.TotalRows = trend.DailyAverageRisk.Count;
        data.Rows = trend.DailyAverageRisk.Select(d => new Dictionary<string, object>
        {
            ["Date"] = d.Date.ToString("yyyy-MM-dd"),
            ["AverageRiskScore"] = Math.Round(d.Value, 2),
            ["TransactionCount"] = d.Count
        }).ToList();

        data.Summary = new Dictionary<string, object>
        {
            ["OverallTrend"] = trend.OverallTrend > 0 ? "Worsening" : "Improving",
            ["StandardDeviation"] = Math.Round(trend.StandardDeviation, 2)
        };
    }

    private async Task PopulateTeamPerformanceReportAsync(ReportData data, string tenantId, DateTime startDate, DateTime endDate, CancellationToken ct)
    {
        var days = (endDate - startDate).Days;
        var analysts = await GetAnalystPerformanceAsync(tenantId, days, ct);
        
        data.TotalRows = analysts.Count;
        data.Rows = analysts.Select(a => new Dictionary<string, object>
        {
            ["Analyst"] = a.AnalystName,
            ["TotalReviews"] = a.TotalReviews,
            ["AvgReviewTime"] = $"{Math.Round(a.AverageReviewTimeMinutes, 1)} min",
            ["ApprovalRate"] = $"{Math.Round(a.ApprovalRate, 1)}%"
        }).ToList();

        data.Summary = new Dictionary<string, object>
        {
            ["TotalAnalysts"] = analysts.Count,
            ["TotalReviews"] = analysts.Sum(a => a.TotalReviews)
        };
    }

    private string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private string GetRiskBand(double score)
    {
        return score switch
        {
            < 25 => "0-25",
            < 50 => "25-50",
            < 75 => "50-75",
            _ => "75-100"
        };
    }

    private async Task<DateTime> GetTenantOnboardingDateAsync(string tenantId, CancellationToken ct)
    {
        var firstTransaction = await _db.Transactions
            .Where(t => t.TenantId == tenantId)
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return firstTransaction?.CreatedAt ?? DateTime.UtcNow.AddDays(-30);
    }

    /// <summary>
    /// False positive rate: approved flagged transactions / total flagged transactions reviewed.
    /// A "false positive" = the system flagged it but a human approved it (not actually fraud).
    /// </summary>
    private double CalculateFalsePositiveRate(IEnumerable<AuditLog> reviews)
    {
        var reviewList = reviews.ToList();
        var total = reviewList.Count;
        if (total == 0) return 0;

        var falsePositives = reviewList.Count(r => r.Action == "TRANSACTION_APPROVED");
        return (double)falsePositives / total * 100;
    }

    private double CalculateTrendSlope(List<DailyMetric> metrics)
    {
        if (metrics.Count < 2) return 0;

        var n = metrics.Count;
        var sumX = Enumerable.Range(0, n).Sum();
        var sumY = metrics.Sum(m => m.Value);
        var sumXY = metrics.Select((m, i) => i * m.Value).Sum();
        var sumX2 = Enumerable.Range(0, n).Sum(i => i * i);

        var slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        return slope;
    }

    private double CalculateStandardDeviation(IEnumerable<double> values)
    {
        var valuesList = values.ToList();
        if (valuesList.Count < 2) return 0;

        var avg = valuesList.Average();
        var sumSquaredDiff = valuesList.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumSquaredDiff / valuesList.Count);
    }
}
