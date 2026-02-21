namespace PayGuardAI.Core.Services;

/// <summary>
/// Email notification service for compliance alerts and reports.
/// Feature flag: FeatureFlags:EmailNotificationsEnabled
/// </summary>
public interface IEmailNotificationService
{
    /// <summary>Send a critical/high-risk transaction alert to the compliance team.</summary>
    Task SendRiskAlertEmailAsync(
        string tenantId,
        string externalId,
        int riskScore,
        string riskLevel,
        decimal amount,
        string currency,
        string senderId,
        CancellationToken cancellationToken = default);

    /// <summary>Send a daily summary report to subscribed users.</summary>
    Task SendDailySummaryEmailAsync(
        string tenantId,
        DailySummaryData summary,
        CancellationToken cancellationToken = default);

    /// <summary>Send an email when a transaction is assigned for review.</summary>
    Task SendReviewAssignmentEmailAsync(
        string tenantId,
        string reviewerEmail,
        string reviewerName,
        string transactionId,
        int riskScore,
        string riskLevel,
        CancellationToken cancellationToken = default);

    /// <summary>Send an invitation email when a new team member is added.</summary>
    Task SendTeamInviteEmailAsync(
        string tenantId,
        string inviteeEmail,
        string inviteeName,
        string inviterName,
        string role,
        CancellationToken cancellationToken = default);

    /// <summary>Send a generic notification email.</summary>
    Task SendNotificationEmailAsync(
        string toEmail,
        string toName,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default);
}

/// <summary>Data for the daily summary report email.</summary>
public record DailySummaryData
{
    public DateTime ReportDate { get; init; }
    public int TotalTransactions { get; init; }
    public int ApprovedTransactions { get; init; }
    public int FlaggedTransactions { get; init; }
    public int RejectedTransactions { get; init; }
    public int PendingReview { get; init; }
    public int ReviewsCompleted { get; init; }
    public decimal TotalVolume { get; init; }
    public string Currency { get; init; } = "USD";
    public int CriticalAlerts { get; init; }
    public int HighRiskAlerts { get; init; }
    public double AverageRiskScore { get; init; }
}
