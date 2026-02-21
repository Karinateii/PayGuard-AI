using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// SMTP-based email notification service for compliance alerts and reports.
/// Works with any SMTP provider — SendGrid, AWS SES, Mailgun, or plain SMTP.
/// Feature flag: FeatureFlags:EmailNotificationsEnabled
/// Config section: Email (SmtpHost, SmtpPort, SmtpUser, SmtpPassword, FromAddress, FromName)
/// </summary>
public class EmailNotificationService : IEmailNotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(
        ApplicationDbContext context,
        IConfiguration config,
        ILogger<EmailNotificationService> logger)
    {
        _context = context;
        _config = config;
        _logger = logger;
    }

    // ── Risk Alert ────────────────────────────────────────────────

    public async Task SendRiskAlertEmailAsync(
        string tenantId,
        string externalId,
        int riskScore,
        string riskLevel,
        decimal amount,
        string currency,
        string senderId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled())
        {
            _logger.LogInformation("Email notifications disabled — skipping risk alert for {TransactionId}", externalId);
            return;
        }

        var recipients = await GetSubscribedRecipientsAsync(tenantId, p => p.RiskAlertsEnabled && riskScore >= p.MinimumRiskScoreForAlert, cancellationToken);
        if (recipients.Count == 0)
        {
            _logger.LogInformation("No recipients subscribed to risk alerts for tenant {TenantId}", tenantId);
            return;
        }

        var appUrl = _config["AppUrl"] ?? "https://payguard-ai-production.up.railway.app";
        var reviewUrl = $"{appUrl}/reviews";

        var (emoji, colour) = riskLevel.ToUpperInvariant() switch
        {
            "CRITICAL" => ("🚨", "#D32F2F"),
            "HIGH"     => ("⚠️", "#F57C00"),
            _          => ("🟡", "#FBC02D")
        };

        var subject = $"{emoji} PayGuard AI — {riskLevel} Risk Transaction Detected";
        var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'/></head>
<body style='font-family:Segoe UI,Roboto,Arial,sans-serif;margin:0;padding:0;background:#f5f5f5;'>
  <div style='max-width:600px;margin:20px auto;background:#fff;border-radius:8px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.1);'>
    <div style='background:{colour};padding:20px 24px;'>
      <h1 style='color:#fff;margin:0;font-size:20px;'>{emoji} {riskLevel} Risk Transaction</h1>
    </div>
    <div style='padding:24px;'>
      <table style='width:100%;border-collapse:collapse;'>
        <tr><td style='padding:8px 0;color:#666;'>Transaction</td><td style='padding:8px 0;font-weight:600;'>{externalId}</td></tr>
        <tr><td style='padding:8px 0;color:#666;'>Risk Score</td><td style='padding:8px 0;font-weight:600;'>{riskScore}/100</td></tr>
        <tr><td style='padding:8px 0;color:#666;'>Risk Level</td><td style='padding:8px 0;font-weight:600;'>{riskLevel}</td></tr>
        <tr><td style='padding:8px 0;color:#666;'>Amount</td><td style='padding:8px 0;font-weight:600;'>{amount:N2} {currency}</td></tr>
        <tr><td style='padding:8px 0;color:#666;'>Sender</td><td style='padding:8px 0;font-weight:600;'>{senderId}</td></tr>
      </table>
      <div style='margin-top:24px;text-align:center;'>
        <a href='{reviewUrl}' style='display:inline-block;background:{colour};color:#fff;padding:12px 32px;border-radius:6px;text-decoration:none;font-weight:600;'>Review Now</a>
      </div>
    </div>
    <div style='padding:16px 24px;background:#fafafa;border-top:1px solid #eee;font-size:12px;color:#999;text-align:center;'>
      PayGuard AI — Compliance Transaction Monitoring
    </div>
  </div>
</body>
</html>";

        foreach (var recipient in recipients)
        {
            await SendEmailAsync(recipient.Email, recipient.DisplayName, subject, html, cancellationToken);
        }

        _logger.LogInformation("Sent risk alert email for {TransactionId} to {Count} recipients", externalId, recipients.Count);
    }

    // ── Daily Summary ─────────────────────────────────────────────

    public async Task SendDailySummaryEmailAsync(
        string tenantId,
        DailySummaryData summary,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled()) return;

        var recipients = await GetSubscribedRecipientsAsync(tenantId, p => p.DailySummaryEnabled, cancellationToken);
        if (recipients.Count == 0) return;

        var appUrl = _config["AppUrl"] ?? "https://payguard-ai-production.up.railway.app";
        var subject = $"📊 PayGuard AI — Daily Summary for {summary.ReportDate:MMM dd, yyyy}";
        var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'/></head>
<body style='font-family:Segoe UI,Roboto,Arial,sans-serif;margin:0;padding:0;background:#f5f5f5;'>
  <div style='max-width:600px;margin:20px auto;background:#fff;border-radius:8px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.1);'>
    <div style='background:#7C4DFF;padding:20px 24px;'>
      <h1 style='color:#fff;margin:0;font-size:20px;'>📊 Daily Summary — {summary.ReportDate:MMM dd, yyyy}</h1>
    </div>
    <div style='padding:24px;'>
      <h2 style='margin:0 0 16px;font-size:16px;color:#333;'>Transaction Overview</h2>
      <div style='display:flex;flex-wrap:wrap;gap:12px;margin-bottom:20px;'>
        {SummaryCard("Total", summary.TotalTransactions.ToString(), "#1976D2")}
        {SummaryCard("Approved", summary.ApprovedTransactions.ToString(), "#388E3C")}
        {SummaryCard("Flagged", summary.FlaggedTransactions.ToString(), "#F57C00")}
        {SummaryCard("Rejected", summary.RejectedTransactions.ToString(), "#D32F2F")}
      </div>
      <table style='width:100%;border-collapse:collapse;'>
        <tr><td style='padding:8px 0;color:#666;'>Pending Review</td><td style='padding:8px 0;font-weight:600;'>{summary.PendingReview}</td></tr>
        <tr><td style='padding:8px 0;color:#666;'>Reviews Completed</td><td style='padding:8px 0;font-weight:600;'>{summary.ReviewsCompleted}</td></tr>
        <tr><td style='padding:8px 0;color:#666;'>Total Volume</td><td style='padding:8px 0;font-weight:600;'>{summary.TotalVolume:N2} {summary.Currency}</td></tr>
        <tr><td style='padding:8px 0;color:#666;'>Avg Risk Score</td><td style='padding:8px 0;font-weight:600;'>{summary.AverageRiskScore:F1}/100</td></tr>
        <tr><td style='padding:8px 0;color:#666;'>Critical Alerts</td><td style='padding:8px 0;font-weight:600;color:#D32F2F;'>{summary.CriticalAlerts}</td></tr>
        <tr><td style='padding:8px 0;color:#666;'>High-Risk Alerts</td><td style='padding:8px 0;font-weight:600;color:#F57C00;'>{summary.HighRiskAlerts}</td></tr>
      </table>
      <div style='margin-top:24px;text-align:center;'>
        <a href='{appUrl}' style='display:inline-block;background:#7C4DFF;color:#fff;padding:12px 32px;border-radius:6px;text-decoration:none;font-weight:600;'>Open Dashboard</a>
      </div>
    </div>
    <div style='padding:16px 24px;background:#fafafa;border-top:1px solid #eee;font-size:12px;color:#999;text-align:center;'>
      PayGuard AI — Compliance Transaction Monitoring
    </div>
  </div>
</body>
</html>";

        foreach (var recipient in recipients)
        {
            await SendEmailAsync(recipient.Email, recipient.DisplayName, subject, html, cancellationToken);
        }

        _logger.LogInformation("Sent daily summary for {Date} to {Count} recipients", summary.ReportDate, recipients.Count);
    }

    // ── Review Assignment ─────────────────────────────────────────

    public async Task SendReviewAssignmentEmailAsync(
        string tenantId,
        string reviewerEmail,
        string reviewerName,
        string transactionId,
        int riskScore,
        string riskLevel,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled()) return;

        // Check if this reviewer has review notifications enabled
        var pref = await _context.NotificationPreferences
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Email == reviewerEmail, cancellationToken);

        if (pref != null && !pref.ReviewAssignmentsEnabled)
        {
            _logger.LogInformation("Reviewer {Email} has review assignment emails disabled", reviewerEmail);
            return;
        }

        var appUrl = _config["AppUrl"] ?? "https://payguard-ai-production.up.railway.app";
        var reviewUrl = $"{appUrl}/reviews";

        var subject = $"📋 PayGuard AI — Transaction Assigned for Your Review";
        var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'/></head>
<body style='font-family:Segoe UI,Roboto,Arial,sans-serif;margin:0;padding:0;background:#f5f5f5;'>
  <div style='max-width:600px;margin:20px auto;background:#fff;border-radius:8px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.1);'>
    <div style='background:#1976D2;padding:20px 24px;'>
      <h1 style='color:#fff;margin:0;font-size:20px;'>📋 Review Assignment</h1>
    </div>
    <div style='padding:24px;'>
      <p style='margin:0 0 16px;color:#333;'>Hi {reviewerName},</p>
      <p style='color:#555;'>A transaction has been assigned to you for compliance review.</p>
      <table style='width:100%;border-collapse:collapse;margin:16px 0;'>
        <tr><td style='padding:8px 0;color:#666;'>Transaction</td><td style='padding:8px 0;font-weight:600;'>{transactionId}</td></tr>
        <tr><td style='padding:8px 0;color:#666;'>Risk Score</td><td style='padding:8px 0;font-weight:600;'>{riskScore}/100</td></tr>
        <tr><td style='padding:8px 0;color:#666;'>Risk Level</td><td style='padding:8px 0;font-weight:600;'>{riskLevel}</td></tr>
      </table>
      <div style='margin-top:24px;text-align:center;'>
        <a href='{reviewUrl}' style='display:inline-block;background:#1976D2;color:#fff;padding:12px 32px;border-radius:6px;text-decoration:none;font-weight:600;'>Start Review</a>
      </div>
    </div>
    <div style='padding:16px 24px;background:#fafafa;border-top:1px solid #eee;font-size:12px;color:#999;text-align:center;'>
      PayGuard AI — Compliance Transaction Monitoring
    </div>
  </div>
</body>
</html>";

        await SendEmailAsync(reviewerEmail, reviewerName, subject, html, cancellationToken);
        _logger.LogInformation("Sent review assignment email to {Email} for transaction {TransactionId}", reviewerEmail, transactionId);
    }

    // ── Team Invite ───────────────────────────────────────────────

    public async Task SendTeamInviteEmailAsync(
        string tenantId,
        string inviteeEmail,
        string inviteeName,
        string inviterName,
        string role,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled()) return;

        var appUrl = _config["AppUrl"] ?? "https://payguard-ai-production.up.railway.app";

        var subject = $"🎉 You've been invited to PayGuard AI";
        var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'/></head>
<body style='font-family:Segoe UI,Roboto,Arial,sans-serif;margin:0;padding:0;background:#f5f5f5;'>
  <div style='max-width:600px;margin:20px auto;background:#fff;border-radius:8px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.1);'>
    <div style='background:#388E3C;padding:20px 24px;'>
      <h1 style='color:#fff;margin:0;font-size:20px;'>🎉 Welcome to PayGuard AI</h1>
    </div>
    <div style='padding:24px;'>
      <p style='margin:0 0 16px;color:#333;'>Hi {inviteeName},</p>
      <p style='color:#555;'><strong>{inviterName}</strong> has invited you to join the compliance team on PayGuard AI as a <strong>{role}</strong>.</p>
      <p style='color:#555;'>PayGuard AI is an intelligent transaction monitoring platform that helps compliance teams detect and review high-risk payments in real time.</p>
      <div style='margin-top:24px;text-align:center;'>
        <a href='{appUrl}' style='display:inline-block;background:#388E3C;color:#fff;padding:12px 32px;border-radius:6px;text-decoration:none;font-weight:600;'>Get Started</a>
      </div>
    </div>
    <div style='padding:16px 24px;background:#fafafa;border-top:1px solid #eee;font-size:12px;color:#999;text-align:center;'>
      PayGuard AI — Compliance Transaction Monitoring
    </div>
  </div>
</body>
</html>";

        await SendEmailAsync(inviteeEmail, inviteeName, subject, html, cancellationToken);
        _logger.LogInformation("Sent team invite email to {Email} invited by {Inviter}", inviteeEmail, inviterName);
    }

    // ── Generic Notification ──────────────────────────────────────

    public async Task SendNotificationEmailAsync(
        string toEmail,
        string toName,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled()) return;
        await SendEmailAsync(toEmail, toName, subject, htmlBody, cancellationToken);
    }

    // ── Core SMTP sender ─────────────────────────────────────────

    private async Task SendEmailAsync(
        string toEmail,
        string toName,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        try
        {
            var smtpHost = _config["Email:SmtpHost"] ?? "smtp.sendgrid.net";
            var smtpPort = int.Parse(_config["Email:SmtpPort"] ?? "587");
            var smtpUser = _config["Email:SmtpUser"] ?? "apikey";
            var smtpPass = _config["Email:SmtpPassword"] ?? "";
            var fromAddr = _config["Email:FromAddress"] ?? "noreply@payguard.ai";
            var fromName = _config["Email:FromName"] ?? "PayGuard AI";

            using var message = new MailMessage();
            message.From = new MailAddress(fromAddr, fromName);
            message.To.Add(new MailAddress(toEmail, toName));
            message.Subject = subject;
            message.Body = htmlBody;
            message.IsBodyHtml = true;

            using var client = new SmtpClient(smtpHost, smtpPort);
            client.Credentials = new NetworkCredential(smtpUser, smtpPass);
            client.EnableSsl = true;

            await client.SendMailAsync(message, cancellationToken);
            _logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            // Graceful degradation — log and continue, don't crash the pipeline
            _logger.LogError(ex, "Failed to send email to {Email}: {Subject}", toEmail, subject);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    private bool IsEnabled()
    {
        var section = _config.GetSection("FeatureFlags");
        var value = section["EmailNotificationsEnabled"];
        return bool.TryParse(value, out var result) && result;
    }

    private async Task<List<NotificationPreference>> GetSubscribedRecipientsAsync(
        string tenantId,
        Func<NotificationPreference, bool> filter,
        CancellationToken cancellationToken)
    {
        var preferences = await _context.NotificationPreferences
            .Where(p => p.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        // If no preferences exist, create a default entry for the auth user
        if (preferences.Count == 0)
        {
            var defaultEmail = _config["Auth:DefaultUser"] ?? "compliance_officer@payguard.ai";
            var defaultPref = new NotificationPreference
            {
                TenantId = tenantId,
                Email = defaultEmail,
                DisplayName = "Compliance Officer"
            };
            _context.NotificationPreferences.Add(defaultPref);
            await _context.SaveChangesAsync(cancellationToken);
            preferences = [defaultPref];
        }

        return preferences.Where(filter).ToList();
    }

    private static string SummaryCard(string label, string value, string colour)
    {
        return $@"<div style='flex:1;min-width:120px;background:{colour}12;border-left:4px solid {colour};padding:12px;border-radius:4px;'>
            <div style='font-size:12px;color:#666;'>{label}</div>
            <div style='font-size:24px;font-weight:700;color:{colour};'>{value}</div>
        </div>";
    }
}

/// <summary>
/// No-op fallback when email notifications are disabled via feature flag.
/// </summary>
public class NoOpEmailNotificationService : IEmailNotificationService
{
    public Task SendRiskAlertEmailAsync(string tenantId, string externalId, int riskScore, string riskLevel, decimal amount, string currency, string senderId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendDailySummaryEmailAsync(string tenantId, DailySummaryData summary, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendReviewAssignmentEmailAsync(string tenantId, string reviewerEmail, string reviewerName, string transactionId, int riskScore, string riskLevel, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendTeamInviteEmailAsync(string tenantId, string inviteeEmail, string inviteeName, string inviterName, string role, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendNotificationEmailAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
