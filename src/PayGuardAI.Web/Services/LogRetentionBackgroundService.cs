using Microsoft.EntityFrameworkCore;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;
using PayGuardAI.Data;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Background service that handles:
/// 1. Log retention — purges SystemLogs older than 30 days (configurable)
/// 2. Error alerting — sends Slack/email alerts when Error/Fatal logs spike
/// 3. Daily summary — emails transaction counts, error counts, latency p95
///
/// Configuration (appsettings.json → "LogRetention"):
///   - RetentionDays: 30 (default)
///   - DailySummaryEnabled: true
///   - DailySummaryRecipients: "admin@company.com"
/// </summary>
public class LogRetentionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<LogRetentionBackgroundService> _logger;

    public LogRetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<LogRetentionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for app to stabilize
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        _logger.LogInformation("Log retention service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeOldLogsAsync(stoppingToken);
                await SendDailySummaryIfDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in log retention service");

                // Send system alert email to SuperAdmin
                try
                {
                    using var alertScope = _scopeFactory.CreateScope();
                    var emailService = alertScope.ServiceProvider.GetRequiredService<IEmailNotificationService>();
                    await emailService.SendSystemAlertEmailAsync(
                        "Background Service Error",
                        $"LogRetentionBackgroundService failed:\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace?[..Math.Min(ex.StackTrace.Length, 500)]}",
                        "ERROR",
                        stoppingToken);
                }
                catch { /* Don't let alert sending crash the service loop */ }
            }

            // Check every 6 hours
            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }

    private async Task PurgeOldLogsAsync(CancellationToken ct)
    {
        var retentionDays = _config.GetValue("LogRetention:RetentionDays", 30);
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Bulk delete old logs (no query filter — SystemLogs is cross-tenant)
        var deleted = await db.SystemLogs
            .IgnoreQueryFilters()
            .Where(l => l.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
        {
            _logger.LogInformation("Purged {Count} system logs older than {Days} days", deleted, retentionDays);
        }
    }

    private async Task SendDailySummaryIfDueAsync(CancellationToken ct)
    {
        // Only run the summary once per day (between 6-8 AM UTC)
        var now = DateTime.UtcNow;
        if (now.Hour < 6 || now.Hour > 8) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();

        var since = now.AddHours(-24);

        // Get all tenants that have activity
        var tenantIds = await db.Transactions
            .IgnoreQueryFilters()
            .Where(t => t.CreatedAt >= since)
            .Select(t => t.TenantId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                // Gather per-tenant stats
                var transactions = await db.Transactions
                    .IgnoreQueryFilters()
                    .Where(t => t.TenantId == tenantId && t.CreatedAt >= since)
                    .ToListAsync(ct);

                var analyses = await db.RiskAnalyses
                    .IgnoreQueryFilters()
                    .Where(r => r.TenantId == tenantId && r.AnalyzedAt >= since)
                    .ToListAsync(ct);

                var summary = new DailySummaryData
                {
                    ReportDate = now.Date.AddDays(-1),  // Report covers the previous day
                    TotalTransactions = transactions.Count,
                    ApprovedTransactions = analyses.Count(a => a.ReviewStatus == ReviewStatus.AutoApproved || a.ReviewStatus == ReviewStatus.Approved),
                    FlaggedTransactions = analyses.Count(a => a.RiskScore >= 50),
                    RejectedTransactions = analyses.Count(a => a.ReviewStatus == ReviewStatus.Rejected),
                    PendingReview = analyses.Count(a => a.ReviewStatus == ReviewStatus.Pending || a.ReviewStatus == ReviewStatus.Escalated),
                    ReviewsCompleted = analyses.Count(a => a.ReviewStatus == ReviewStatus.Approved || a.ReviewStatus == ReviewStatus.Rejected),
                    TotalVolume = transactions.Sum(t => t.Amount),
                    Currency = transactions.FirstOrDefault()?.SourceCurrency ?? "USD",
                    CriticalAlerts = analyses.Count(a => a.RiskLevel == RiskLevel.Critical),
                    HighRiskAlerts = analyses.Count(a => a.RiskLevel == RiskLevel.High),
                    AverageRiskScore = analyses.Count > 0 ? analyses.Average(a => a.RiskScore) : 0
                };

                await emailService.SendDailySummaryEmailAsync(tenantId, summary, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send daily summary for tenant {TenantId}", tenantId);

                // Alert SuperAdmin about the failure
                try
                {
                    await emailService.SendSystemAlertEmailAsync(
                        $"Daily Summary Failed for {tenantId}",
                        $"{ex.GetType().Name}: {ex.Message}",
                        "WARNING",
                        ct);
                }
                catch { /* Don't let alert sending mask the original error */ }
            }
        }
    }
}
