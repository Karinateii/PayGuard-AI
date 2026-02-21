namespace PayGuardAI.Core.Services;

/// <summary>
/// Abstraction for recording application metrics.
/// Implemented by PrometheusMetricsService in production (Web project).
/// Keeps prometheus-net out of the Core/Data projects.
/// </summary>
public interface IMetricsService
{
    /// <summary>Record a transaction being processed with its risk level and outcome (auto_approved, flagged, blocked).</summary>
    void RecordTransactionProcessed(string riskLevel, string outcome);

    /// <summary>Record the risk score assigned to a transaction.</summary>
    void RecordRiskScore(double score);

    /// <summary>Record a webhook arriving from a payment provider.</summary>
    void RecordWebhookReceived(string provider);

    /// <summary>Record a manual review being completed (approved, rejected, escalated).</summary>
    void RecordReviewCompleted(string outcome);
}
