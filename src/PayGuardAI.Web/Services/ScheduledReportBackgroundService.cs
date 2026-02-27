using Microsoft.EntityFrameworkCore;
using PayGuardAI.Core.Services;
using PayGuardAI.Data;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Background service that delivers scheduled custom reports via email.
///
/// How it works:
///   1. Every <c>CheckIntervalMinutes</c> (default: 60 min), scans all CustomReports
///      where IsScheduled == true and EmailRecipients is not empty.
///   2. For each scheduled report, checks if it's due based on ScheduleCron:
///      - "daily"   → once per day (checks if last run was >24h ago)
///      - "weekly"  → once per week (checks if last run was >7 days ago)
///      - "monthly" → once per month (checks if last run was >30 days ago)
///   3. Runs the report via IAdvancedAnalyticsService, exports to CSV,
///      and emails to all recipients via IEmailNotificationService.
///
/// Configuration (appsettings.json → "ScheduledReports"):
///   - Enabled: true/false (default: true)
///   - CheckIntervalMinutes: how often to check (default: 60)
/// </summary>
public class ScheduledReportBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ScheduledReportBackgroundService> _logger;

    public ScheduledReportBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<ScheduledReportBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _config.GetValue("ScheduledReports:Enabled", true);
        if (!enabled)
        {
            _logger.LogInformation("Scheduled report delivery is disabled");
            return;
        }

        var intervalMinutes = _config.GetValue("ScheduledReports:CheckIntervalMinutes", 60);
        _logger.LogInformation("Scheduled report service started — checking every {Interval} minutes", intervalMinutes);

        // Initial delay to let app finish startup
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessScheduledReportsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing scheduled reports");
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    private async Task ProcessScheduledReportsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var analyticsService = scope.ServiceProvider.GetRequiredService<IAdvancedAnalyticsService>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();

        // Find all scheduled reports with email recipients
        var scheduledReports = await db.CustomReports
            .Where(r => r.IsScheduled && r.EmailRecipients != null && r.EmailRecipients != "")
            .ToListAsync(ct);

        if (scheduledReports.Count == 0) return;

        _logger.LogInformation("Found {Count} scheduled reports to evaluate", scheduledReports.Count);

        foreach (var report in scheduledReports)
        {
            try
            {
                if (!IsDue(report))
                    continue;

                _logger.LogInformation("Running scheduled report '{ReportName}' for tenant {TenantId}",
                    report.Name, report.TenantId);

                // Set dynamic date range based on frequency
                var now = DateTime.UtcNow;
                report.EndDate = now;
                report.StartDate = (report.ScheduleCron?.ToLowerInvariant()) switch
                {
                    "daily"   => now.AddDays(-1),
                    "weekly"  => now.AddDays(-7),
                    "monthly" => now.AddDays(-30),
                    _         => now.AddDays(-1),
                };
                await db.SaveChangesAsync(ct);

                // Run the report
                var data = await analyticsService.RunReportAsync(report.Id, ct);

                if (data.TotalRows == 0)
                {
                    _logger.LogInformation("Report '{ReportName}' returned 0 rows — skipping email", report.Name);
                    // Still update the timestamp so we don't keep re-running
                    UpdateLastRun(report);
                    await db.SaveChangesAsync(ct);
                    continue;
                }

                // Export to CSV
                var csvBytes = await analyticsService.ExportReportToCsvAsync(data, ct);

                // Build HTML email body
                var htmlBody = BuildReportEmailHtml(report, data);

                // Send to each recipient
                var recipients = report.EmailRecipients!
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var email in recipients)
                {
                    try
                    {
                        await emailService.SendNotificationEmailAsync(
                            toEmail: email,
                            toName: email.Split('@')[0],
                            subject: $"[PayGuard AI] Scheduled Report: {report.Name} — {DateTime.UtcNow:yyyy-MM-dd}",
                            htmlBody: htmlBody,
                            cancellationToken: ct);

                        _logger.LogInformation("Sent scheduled report '{ReportName}' to {Email}", report.Name, email);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send report '{ReportName}' to {Email}", report.Name, email);
                    }
                }

                // Mark as run
                UpdateLastRun(report);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error running scheduled report '{ReportName}'", report.Name);
            }
        }
    }

    /// <summary>
    /// Determines if a scheduled report is due based on its ScheduleCron value.
    /// Uses CreatedAt as the "last run" timestamp (we update it after each run).
    /// </summary>
    private static bool IsDue(CustomReport report)
    {
        var lastRun = report.CreatedAt; // We repurpose CreatedAt as last-run marker
        var now = DateTime.UtcNow;

        return (report.ScheduleCron?.ToLowerInvariant()) switch
        {
            "daily"   => (now - lastRun).TotalHours >= 23, // ~daily, with some tolerance
            "weekly"  => (now - lastRun).TotalDays >= 6.5,
            "monthly" => (now - lastRun).TotalDays >= 29,
            _         => (now - lastRun).TotalDays >= 1,   // Default to daily
        };
    }

    /// <summary>
    /// Updates the report's CreatedAt to now (acting as "last run" timestamp).
    /// We use CreatedAt because we don't want to add a DB migration for a new column.
    /// </summary>
    private static void UpdateLastRun(CustomReport report)
    {
        report.CreatedAt = DateTime.UtcNow;
    }

    private static string BuildReportEmailHtml(CustomReport report, ReportData data)
    {
        var rows = data.Rows.Take(25); // Limit email preview to 25 rows
        var headers = data.Rows.FirstOrDefault()?.Keys.ToList() ?? [];

        var tableRows = string.Join("",
            rows.Select(row =>
                "<tr>" + string.Join("",
                    headers.Select(h =>
                        $"<td style='padding:6px 10px;border-bottom:1px solid #eee;font-size:13px;'>{row.GetValueOrDefault(h, "")}</td>"))
                + "</tr>"));

        var headerRow = "<tr>" + string.Join("",
            headers.Select(h =>
                $"<th style='padding:8px 10px;background:#f5f5f5;border-bottom:2px solid #ddd;text-align:left;font-size:13px;'>{h}</th>"))
            + "</tr>";

        return $"""
            <div style="font-family:Arial,sans-serif;max-width:700px;margin:0 auto;">
                <div style="background:#1a237e;color:white;padding:20px;border-radius:8px 8px 0 0;">
                    <h2 style="margin:0;">📊 Scheduled Report: {report.Name}</h2>
                    <p style="margin:8px 0 0 0;opacity:0.9;">Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</p>
                </div>
                <div style="padding:20px;border:1px solid #e0e0e0;border-top:none;">
                    <p><strong>Report Type:</strong> {report.ReportType}</p>
                    <p><strong>Total Records:</strong> {data.TotalRows}</p>
                    {(data.Summary.Any() ? "<p><strong>Summary:</strong> " + string.Join(", ", data.Summary.Select(kvp => $"{kvp.Key}: {kvp.Value}")) + "</p>" : "")}
                    
                    <table style="width:100%;border-collapse:collapse;margin-top:16px;">
                        {headerRow}
                        {tableRows}
                    </table>
                    
                    {(data.TotalRows > 25 ? $"<p style='color:#666;margin-top:12px;font-size:13px;'>Showing 25 of {data.TotalRows} rows. Log in to PayGuard AI for the full dataset.</p>" : "")}
                </div>
                <div style="padding:16px;background:#f5f5f5;border-radius:0 0 8px 8px;border:1px solid #e0e0e0;border-top:none;">
                    <p style="margin:0;color:#666;font-size:12px;">
                        This report was automatically generated by PayGuard AI.
                        To manage scheduled reports, visit the Advanced Reports page.
                    </p>
                </div>
            </div>
            """;
    }
}
