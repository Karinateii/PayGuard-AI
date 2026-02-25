using PayGuardAI.Core.Entities;

namespace PayGuardAI.Core.Services;

// ═════════════════════════════════════════════════════════════════════════
// ML Scoring — runs predictions against the trained model
// ═════════════════════════════════════════════════════════════════════════

/// <summary>
/// Scores transactions using a trained ML model.
/// Thread-safe: caches loaded models per tenant in memory.
/// </summary>
public interface IMLScoringService
{
    /// <summary>
    /// Score a transaction using the active ML model for the tenant.
    /// Returns null if no model is loaded or ML scoring is disabled.
    /// </summary>
    Task<MLPredictionResult?> ScoreTransactionAsync(
        Transaction transaction,
        CustomerProfile profile,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Load (or reload) the active model for a tenant into memory.
    /// Called on startup and after training a new model.
    /// </summary>
    Task<bool> LoadModelAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a model is loaded and ready for the given tenant.
    /// </summary>
    bool IsModelAvailable(string tenantId);

    /// <summary>
    /// Evict the cached model for a tenant (e.g., after deactivation).
    /// </summary>
    void EvictModel(string tenantId);
}

// ═════════════════════════════════════════════════════════════════════════
// ML Training — builds models from HITL feedback
// ═════════════════════════════════════════════════════════════════════════

/// <summary>
/// Trains ML models from human-reviewed transaction data.
/// Uses HITL feedback (Approved = legitimate, Rejected = fraud) as labels.
/// </summary>
public interface IMLTrainingService
{
    /// <summary>
    /// Train a new model from all labeled data for the tenant.
    /// Performs cross-validation, evaluates metrics, and persists the model.
    /// </summary>
    Task<MLTrainingResult> TrainModelAsync(string tenantId, string trainedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get info about the currently active model for a tenant.
    /// </summary>
    Task<MLModelInfo?> GetActiveModelInfoAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all trained models for a tenant (history).
    /// </summary>
    Task<List<MLModelInfo>> GetAllModelsAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get statistics about available training data.
    /// </summary>
    Task<MLTrainingDataStats> GetTrainingDataStatsAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activate a specific model version for scoring.
    /// Deactivates any previously active model.
    /// </summary>
    Task ActivateModelAsync(Guid modelId, string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivate the current model (revert to rule-only scoring).
    /// </summary>
    Task DeactivateModelAsync(string tenantId, CancellationToken cancellationToken = default);
}

// ═════════════════════════════════════════════════════════════════════════
// DTOs
// ═════════════════════════════════════════════════════════════════════════

/// <summary>
/// Result of an ML prediction for a single transaction.
/// </summary>
public class MLPredictionResult
{
    /// <summary>
    /// Probability that the transaction is fraudulent (0.0 – 1.0).
    /// </summary>
    public float FraudProbability { get; set; }

    /// <summary>
    /// Points to add to the risk score (0 – 40, scaled from probability).
    /// </summary>
    public int ScoreContribution { get; set; }

    /// <summary>
    /// Human-readable explanation of top contributing features.
    /// </summary>
    public string TopFeatures { get; set; } = string.Empty;

    /// <summary>
    /// Model version that produced this prediction.
    /// </summary>
    public string ModelVersion { get; set; } = string.Empty;

    /// <summary>
    /// Serialize to JSON for RiskFactor.ContextData.
    /// </summary>
    public string ToJson() =>
        $"{{\"fraudProbability\": {FraudProbability:F4}, \"scoreContribution\": {ScoreContribution}, " +
        $"\"modelVersion\": \"{ModelVersion}\"}}";
}

/// <summary>
/// Result of a training run.
/// </summary>
public class MLTrainingResult
{
    public bool Success { get; set; }
    public Guid? ModelId { get; set; }
    public string? Version { get; set; }
    public double Accuracy { get; set; }
    public double AUC { get; set; }
    public double F1Score { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }
    public int TrainingSamples { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Summary info about a trained model (without the binary data).
/// </summary>
public class MLModelInfo
{
    public Guid Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public string TrainerName { get; set; } = string.Empty;
    public int TrainingSamples { get; set; }
    public int FraudSamples { get; set; }
    public int LegitSamples { get; set; }
    public double Accuracy { get; set; }
    public double AUC { get; set; }
    public double F1Score { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }
    public bool IsActive { get; set; }
    public DateTime TrainedAt { get; set; }
    public string TrainedBy { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

/// <summary>
/// Statistics about available HITL-labeled training data.
/// </summary>
public class MLTrainingDataStats
{
    public int TotalLabeled { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }
    public int MinimumRequired { get; set; } = 50;
    public int MinimumPerClass { get; set; } = 5;

    /// <summary>
    /// True when enough labeled data exists (both classes) to train a model.
    /// </summary>
    public bool IsReadyForTraining =>
        TotalLabeled >= MinimumRequired
        && ApprovedCount >= MinimumPerClass
        && RejectedCount >= MinimumPerClass;
}
