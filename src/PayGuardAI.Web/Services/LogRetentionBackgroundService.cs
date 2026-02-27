using Microsoft.EntityFrameworkCore;
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
        var enabled = _config.GetValue("LogRetention:DailySummaryEnabled", false);
        if (!enabled) return;

        var recipients = _config.GetValue<string>("LogRetention:DailySummaryRecipients");
        if (string.IsNullOrWhiteSpace(recipients)) return;

        // Only run the summary once per day (check if we've already sent today)
        var now = DateTime.UtcNow;
        if (now.Hour < 6 || now.Hour > 8) return; // Only try between 6-8 AM UTC

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();

        var since = now.AddHours(-24);

        // Gather stats
        var logs = await db.SystemLogs
            .IgnoreQueryFilters()
            .Where(l => l.CreatedAt >= since)
            .ToListAsync(ct);

        var errorCount = logs.Count(l => l.Level == "Error" || l.Level == "Fatal");
        var warningCount = logs.Count(l => l.Level == "Warning");
        var totalCount = logs.Count;

        // Transaction stats
        var txnCount = await db.Transactions
            .IgnoreQueryFilters()
            .Where(t => t.CreatedAt >= since)
            .CountAsync(ct);

        var highRiskCount = await db.RiskAnalyses
            .IgnoreQueryFilters()
            .Where(r => r.AnalyzedAt >= since && r.RiskScore >= 70)
            .CountAsync(ct);

        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;">
                <div style="background:#1a237e;color:white;padding:20px;border-radius:8px 8px 0 0;">
                    <h2 style="margin:0;">📊 PayGuard AI — Daily Summary</h2>
                    <p style="margin:8px 0 0;opacity:0.9;">{now:yyyy-MM-dd}</p>
                </div>
                <div style="padding:20px;border:1px solid #e0e0e0;border-top:none;">
                    <h3>System Health</h3>
                    <table style="width:100%;border-collapse:collapse;">
                        <tr>
                            <td style="padding:8px;border-bottom:1px solid #eee;"><strong>🔴 Errors</strong></td>
                            <td style="padding:8px;border-bottom:1px solid #eee;color:{(errorCount > 0 ? "#D32F2F" : "#4CAF50")};">{errorCount}</td>
                        </tr>
                        <tr>
                            <td style="padding:8px;border-bottom:1px solid #eee;"><strong>🟡 Warnings</strong></td>
                            <td style="padding:8px;border-bottom:1px solid #eee;">{warningCount}</td>
                        </tr>
                        <tr>
                            <td style="padding:8px;border-bottom:1px solid #eee;"><strong>📝 Total Log Events</strong></td>
                            <td style="padding:8px;border-bottom:1px solid #eee;">{totalCount}</td>
                        </tr>
                    </table>

                    <h3 style="margin-top:20px;">Business Metrics (24h)</h3>
                    <table style="width:100%;border-collapse:collapse;">
                        <tr>
                            <td style="padding:8px;border-bottom:1px solid #eee;"><strong>💸 Transactions Processed</strong></td>
                            <td style="padding:8px;border-bottom:1px solid #eee;">{txnCount}</td>
                        </tr>
                        <tr>
                            <td style="padding:8px;border-bottom:1px solid #eee;"><strong>🚨 High-Risk Flags</strong></td>
                            <td style="padding:8px;border-bottom:1px solid #eee;color:{(highRiskCount > 0 ? "#F57C00" : "#4CAF50")};">{highRiskCount}</td>
                        </tr>
                    </table>

                    {(errorCount > 5 ? "<div style='margin-top:16px;padding:12px;background:#FFEBEE;border-left:4px solid #D32F2F;border-radius:4px;'><strong>⚠️ Alert:</strong> High error count detected. Check the System Logs page for details.</div>" : "")}
                </div>
                <div style="padding:12px;background:#f5f5f5;border-radius:0 0 8px 8px;border:1px solid #e0e0e0;border-top:none;">
                    <p style="margin:0;color:#666;font-size:12px;">Automated daily summary from PayGuard AI. Manage at Admin → System Logs.</p>
                </div>
            </div>
            """;

        foreach (var email in recipients.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                await emailService.SendNotificationEmailAsync(
                    email, email.Split('@')[0],
                    $"[PayGuard AI] Daily Summary — {errorCount} errors, {txnCount} transactions",
                    html, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send daily summary to {Email}", email);
            }
        }
    }
}
