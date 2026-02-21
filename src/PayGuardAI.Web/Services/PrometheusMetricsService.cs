using PayGuardAI.Core.Services;
using Prometheus;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Prometheus implementation of IMetricsService.
/// Exposes counters and histograms at /metrics for Grafana/Prometheus scraping.
///
/// Metrics exposed:
///   payguard_transactions_total{risk_level, outcome}     — counter
///   payguard_risk_scores                                 — histogram (buckets: 0-100)
///   payguard_webhooks_total{provider}                    — counter
///   payguard_reviews_total{outcome}                      — counter
/// </summary>
public class PrometheusMetricsService : IMetricsService
{
    // Counters — total count, never decreases
    private readonly Counter _transactionsTotal = Metrics.CreateCounter(
        "payguard_transactions_total",
        "Total transactions processed by PayGuard AI",
        new CounterConfiguration { LabelNames = ["risk_level", "outcome"] });

    private readonly Counter _webhooksTotal = Metrics.CreateCounter(
        "payguard_webhooks_total",
        "Total webhooks received from payment providers",
        new CounterConfiguration { LabelNames = ["provider"] });

    private readonly Counter _reviewsTotal = Metrics.CreateCounter(
        "payguard_reviews_total",
        "Total manual reviews completed",
        new CounterConfiguration { LabelNames = ["outcome"] });

    // Histogram — distribution of risk scores (0 to 100)
    private readonly Histogram _riskScoreHistogram = Metrics.CreateHistogram(
        "payguard_risk_scores",
        "Distribution of risk scores assigned to transactions",
        new HistogramConfiguration
        {
            Buckets = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100]
        });

    public void RecordTransactionProcessed(string riskLevel, string outcome)
        => _transactionsTotal.WithLabels(riskLevel.ToLowerInvariant(), outcome.ToLowerInvariant()).Inc();

    public void RecordRiskScore(double score)
        => _riskScoreHistogram.Observe(score);

    public void RecordWebhookReceived(string provider)
        => _webhooksTotal.WithLabels(provider.ToLowerInvariant()).Inc();

    public void RecordReviewCompleted(string outcome)
        => _reviewsTotal.WithLabels(outcome.ToLowerInvariant()).Inc();
}
