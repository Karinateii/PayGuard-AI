namespace PayGuardAI.Core.Services;

/// <summary>
/// Alerting service for critical compliance events.
/// </summary>
public interface IAlertingService
{
    /// <summary>Emit a plain-text alert for a system event.</summary>
    Task AlertAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Emit a rich structured alert for a high/critical risk transaction.
    /// Includes risk score, amount, sender, and a direct link to the review queue.
    /// </summary>
    Task AlertTransactionAsync(
        string tenantId,
        string externalId,
        int riskScore,
        string riskLevel,
        decimal amount,
        string currency,
        string senderId,
        CancellationToken cancellationToken = default);
}
