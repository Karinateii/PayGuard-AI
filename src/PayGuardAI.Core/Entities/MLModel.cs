namespace PayGuardAI.Core.Entities;

/// <summary>
/// Tracks trained ML models for risk scoring.
/// Stores model binary data, version, and performance metrics.
/// </summary>
public class MLModel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Tenant this model belongs to. Each tenant trains their own model.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable version string (e.g., "v1", "v2").
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// ML trainer algorithm used (e.g., "FastTree", "LightGbm").
    /// </summary>
    public string TrainerName { get; set; } = "FastTree";

    /// <summary>
    /// Total labeled samples used for training.
    /// </summary>
    public int TrainingSamples { get; set; }

    /// <summary>
    /// Number of fraud (rejected) samples in training data.
    /// </summary>
    public int FraudSamples { get; set; }

    /// <summary>
    /// Number of legitimate (approved) samples in training data.
    /// </summary>
    public int LegitSamples { get; set; }

    // ── Model Performance Metrics ───────────────────────────────────────

    /// <summary>
    /// Overall accuracy (correct predictions / total).
    /// </summary>
    public double Accuracy { get; set; }

    /// <summary>
    /// Area Under the ROC Curve — primary ranking metric.
    /// </summary>
    public double AUC { get; set; }

    /// <summary>
    /// F1 Score — harmonic mean of precision and recall.
    /// </summary>
    public double F1Score { get; set; }

    /// <summary>
    /// Precision for fraud class (true positives / predicted positives).
    /// </summary>
    public double PositivePrecision { get; set; }

    /// <summary>
    /// Recall for fraud class (true positives / actual positives).
    /// </summary>
    public double PositiveRecall { get; set; }

    // ── Model State ─────────────────────────────────────────────────────

    /// <summary>
    /// Whether this model is currently used for scoring.
    /// Only one model per tenant should be active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Serialized ML.NET model binary. Stored in DB for Railway compatibility
    /// (ephemeral filesystem). Loaded into memory on first prediction.
    /// </summary>
    public byte[]? ModelData { get; set; }

    /// <summary>
    /// When the model was trained.
    /// </summary>
    public DateTime TrainedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who triggered the training (email or "system" for auto-retrain).
    /// </summary>
    public string TrainedBy { get; set; } = "system";

    /// <summary>
    /// Optional notes about this model version.
    /// </summary>
    public string? Notes { get; set; }
}
