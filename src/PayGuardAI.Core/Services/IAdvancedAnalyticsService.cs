using PayGuardAI.Core.Entities;

namespace PayGuardAI.Core.Services;

/// <summary>
/// Advanced analytics service for custom reports, team performance, and ROI metrics.
/// </summary>
public interface IAdvancedAnalyticsService
{
    // Custom Reports
    Task<CustomReport> CreateReportAsync(string tenantId, CustomReport report, string createdBy, CancellationToken ct = default);
    Task<List<CustomReport>> GetReportsAsync(string tenantId, CancellationToken ct = default);
    Task<CustomReport?> GetReportByIdAsync(Guid reportId, CancellationToken ct = default);
    Task DeleteReportAsync(Guid reportId, CancellationToken ct = default);
    Task<ReportData> RunReportAsync(Guid reportId, CancellationToken ct = default);
    Task<byte[]> ExportReportToCsvAsync(ReportData data, CancellationToken ct = default);
    Task<byte[]> ExportReportToPdfAsync(ReportData data, CancellationToken ct = default);

    // Risk Trends
    Task<RiskTrendAnalysis> GetRiskTrendAnalysisAsync(string tenantId, DateTime startDate, DateTime endDate, CancellationToken ct = default);
    Task<List<CorridorRiskScore>> GetCorridorRiskHeatmapAsync(string tenantId, int days = 30, CancellationToken ct = default);
    Task<List<DailyMetric>> GetFlaggedTransactionRateAsync(string tenantId, int days = 30, CancellationToken ct = default);

    // Team Performance
    Task<List<AnalystPerformance>> GetAnalystPerformanceAsync(string tenantId, int days = 30, CancellationToken ct = default);
    Task<TeamPerformanceMetrics> GetTeamPerformanceMetricsAsync(string tenantId, int days = 30, CancellationToken ct = default);
    Task<List<ReviewTimeDistribution>> GetReviewTimeDistributionAsync(string tenantId, int days = 30, CancellationToken ct = default);

    // Decision Consistency
    Task<DecisionConsistencyMetrics> GetDecisionConsistencyAsync(string tenantId, int days = 30, CancellationToken ct = default);
    Task<List<OutlierAnalyst>> GetOutlierAnalystsAsync(string tenantId, int days = 30, CancellationToken ct = default);

    // ROI Calculator
    Task<RoiMetrics> CalculateRoiAsync(string tenantId, DateTime? startDate = null, CancellationToken ct = default);
}

/// <summary>
/// Custom report definition that users can create and schedule.
/// </summary>
public class CustomReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string ReportType { get; set; } = "transactions"; // transactions, risk-trends, team-performance
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Filters { get; set; } // JSON serialized filters
    public string? Grouping { get; set; } // corridor, risk-level, analyst, etc.
    public bool IsScheduled { get; set; }
    public string? ScheduleCron { get; set; } // Cron expression for scheduled delivery
    public string? EmailRecipients { get; set; } // Comma-separated
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Result data from running a custom report.
/// </summary>
public class ReportData
{
    public Guid ReportId { get; set; }
    public string ReportName { get; set; } = "";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public int TotalRows { get; set; }
    public List<Dictionary<string, object>> Rows { get; set; } = [];
    public Dictionary<string, object> Summary { get; set; } = [];
}

/// <summary>
/// Risk trend analysis over time.
/// </summary>
public class RiskTrendAnalysis
{
    public List<DailyMetric> DailyAverageRisk { get; set; } = [];
    public List<DailyMetric> DailyHighRiskCount { get; set; } = [];
    public double OverallTrend { get; set; } // Positive = getting worse, Negative = improving
    public double StandardDeviation { get; set; }
}

/// <summary>
/// Risk score by corridor (source country → destination country).
/// </summary>
public class CorridorRiskScore
{
    public string SourceCountry { get; set; } = "";
    public string DestinationCountry { get; set; } = "";
    public int TransactionCount { get; set; }
    public double AverageRiskScore { get; set; }
    public int HighRiskCount { get; set; }
}

/// <summary>
/// Daily metric data point (used for trend charts).
/// </summary>
public class DailyMetric
{
    public DateTime Date { get; set; }
    public double Value { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Performance metrics for a single analyst.
/// </summary>
public class AnalystPerformance
{
    public string AnalystEmail { get; set; } = "";
    public string AnalystName { get; set; } = "";
    public int TotalReviews { get; set; }
    public double AverageReviewTimeMinutes { get; set; }
    public int ApprovalsCount { get; set; }
    public int RejectionsCount { get; set; }
    public int EscalationsCount { get; set; }
    public double ApprovalRate { get; set; }
    public int OverturnedDecisions { get; set; } // Decisions later changed by another analyst
}

/// <summary>
/// Aggregate team performance metrics.
/// </summary>
public class TeamPerformanceMetrics
{
    public int TotalReviews { get; set; }
    public double AverageReviewTimeMinutes { get; set; }
    public double MedianReviewTimeMinutes { get; set; }
    public int ActiveAnalysts { get; set; }
    public double InterRaterAgreement { get; set; } // % of cases where analysts agree
    public List<AnalystPerformance> TopPerformers { get; set; } = [];
}

/// <summary>
/// Distribution of review times (for histogram).
/// </summary>
public class ReviewTimeDistribution
{
    public string TimeBucket { get; set; } = ""; // "0-5 min", "5-15 min", etc.
    public int Count { get; set; }
}

/// <summary>
/// Decision consistency metrics across all analysts.
/// </summary>
public class DecisionConsistencyMetrics
{
    public Dictionary<string, DecisionRateByRiskBand> ApprovalRatesByRiskScore { get; set; } = [];
    public double OverallConsistency { get; set; } // 0-1 score
    public int TotalInconsistencies { get; set; }
}

/// <summary>
/// Decision rate for a specific risk score band (0-25, 25-50, etc.).
/// </summary>
public class DecisionRateByRiskBand
{
    public string RiskBand { get; set; } = "";
    public int TotalDecisions { get; set; }
    public int Approvals { get; set; }
    public int Rejections { get; set; }
    public double ApprovalRate { get; set; }
}

/// <summary>
/// Analyst whose decisions differ significantly from team average.
/// </summary>
public class OutlierAnalyst
{
    public string AnalystEmail { get; set; } = "";
    public string AnalystName { get; set; } = "";
    public string OutlierType { get; set; } = ""; // "too-lenient", "too-strict"
    public double DeviationFromMean { get; set; }
    public double ApprovalRate { get; set; }
    public double TeamAverageApprovalRate { get; set; }
}

/// <summary>
/// ROI metrics showing the value PayGuard AI provides.
/// </summary>
public class RoiMetrics
{
    public int TotalTransactionsMonitored { get; set; }
    public int FraudulentTransactionsCaught { get; set; }
    public decimal EstimatedFraudValuePrevented { get; set; }
    public int FalsePositives { get; set; }
    public int TruePositives { get; set; }
    public double Precision { get; set; } // TP / (TP + FP)
    public double Recall { get; set; } // TP / (TP + FN) — requires knowing false negatives
    public double FalsePositiveRate { get; set; }
    public double FalsePositiveRateAtOnboarding { get; set; }
    public double FalsePositiveReduction { get; set; } // % improvement since onboarding
    public decimal CostSavings { get; set; } // Fraud prevented - review costs
    public int DaysSinceOnboarding { get; set; }
}
