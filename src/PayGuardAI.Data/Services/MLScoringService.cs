using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;
using PayGuardAI.Data.ML;

namespace PayGuardAI.Data.Services;

/// <summary>
/// ML scoring service — loads trained models from DB and scores transactions.
/// Uses a static model cache so the heavy model binary is loaded once per tenant
/// and shared across all scoped instances.
/// </summary>
public class MLScoringService : IMLScoringService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<MLScoringService> _logger;

    // Static cache: tenant → (ML context, loaded transformer, model version)
    // Thread-safe: ConcurrentDictionary + ITransformer is read-safe after creation
    private static readonly ConcurrentDictionary<string, CachedModel> _modelCache = new();

    /// <summary>
    /// Maximum ML score contribution to the risk score (out of 100).
    /// </summary>
    private const int MaxMLScoreContribution = 40;

    public MLScoringService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        ILogger<MLScoringService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<MLPredictionResult?> ScoreTransactionAsync(
        Transaction transaction,
        CustomerProfile profile,
        CancellationToken cancellationToken = default)
    {
        var tenantId = transaction.TenantId;

        if (!_modelCache.TryGetValue(tenantId, out var cached))
        {
            // Try to load on first use
            var loaded = await LoadModelAsync(tenantId, cancellationToken);
            if (!loaded || !_modelCache.TryGetValue(tenantId, out cached))
            {
                return null;
            }
        }

        try
        {
            // Extract features (needs DB for velocity)
            await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
            context.SetTenantId(tenantId);

            var features = await MLFeatureExtractor.ExtractFeaturesAsync(
                transaction, profile, context, cancellationToken);

            // Run prediction
            var engine = cached.Context.Model.CreatePredictionEngine<TransactionMLInput, TransactionMLOutput>(cached.Model);
            var prediction = engine.Predict(features);

            // Map probability to score contribution (0 – MaxMLScoreContribution)
            int scoreContribution = (int)Math.Round(prediction.Probability * MaxMLScoreContribution);

            // Generate feature importance explanation
            var topFeatures = GetTopFeatureExplanation(features, prediction.Probability);

            _logger.LogInformation(
                "ML scored transaction {TransactionId} for tenant {TenantId}: P(fraud)={Probability:F3}, contribution={Score} (model {Version})",
                transaction.Id, tenantId, prediction.Probability, scoreContribution, cached.Version);

            return new MLPredictionResult
            {
                FraudProbability = prediction.Probability,
                ScoreContribution = scoreContribution,
                TopFeatures = topFeatures,
                ModelVersion = cached.Version
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ML scoring failed for transaction {TransactionId}, tenant {TenantId}", transaction.Id, tenantId);
            return null; // Graceful degradation: fall back to rule-only scoring
        }
    }

    public async Task<bool> LoadModelAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
            context.SetTenantId(tenantId);

            var activeModel = await context.Set<MLModel>()
                .Where(m => m.IsActive && m.ModelData != null)
                .OrderByDescending(m => m.TrainedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (activeModel?.ModelData == null)
            {
                _logger.LogDebug("No active ML model found for tenant {TenantId}", tenantId);
                return false;
            }

            var mlContext = new MLContext(seed: 42);
            using var stream = new MemoryStream(activeModel.ModelData);
            var model = mlContext.Model.Load(stream, out _);

            _modelCache[tenantId] = new CachedModel(mlContext, model, activeModel.Version);

            _logger.LogInformation(
                "Loaded ML model {Version} for tenant {TenantId} ({Samples} samples, AUC={AUC:F3})",
                activeModel.Version, tenantId, activeModel.TrainingSamples, activeModel.AUC);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load ML model for tenant {TenantId}", tenantId);
            return false;
        }
    }

    public bool IsModelAvailable(string tenantId) => _modelCache.ContainsKey(tenantId);

    public void EvictModel(string tenantId)
    {
        _modelCache.TryRemove(tenantId, out _);
        _logger.LogInformation("Evicted ML model cache for tenant {TenantId}", tenantId);
    }

    /// <summary>
    /// Generate a human-readable explanation of which features most influenced the prediction.
    /// Uses feature-value heuristics (not SHAP) for fast, interpretable explanations.
    /// </summary>
    private static string GetTopFeatureExplanation(TransactionMLInput input, float probability)
    {
        var featureValues = MLFeatureExtractor.ToArray(input);
        var explanations = new List<(string Name, float Value, string Reason)>();

        // Identify the most notable features that likely drove the prediction
        if (input.Amount > 5000)
            explanations.Add(("Amount", input.Amount, $"high amount (${input.Amount:N0})"));
        if (input.IsHighRiskCountry > 0)
            explanations.Add(("Geography", 1f, "high-risk country involved"));
        if (input.IsNightTime > 0)
            explanations.Add(("Timing", 1f, "unusual hour (2-5 AM)"));
        if (input.Velocity24h > 5)
            explanations.Add(("Velocity", input.Velocity24h, $"{input.Velocity24h} transactions in 24h"));
        if (input.AmountDeviation > 3)
            explanations.Add(("Deviation", input.AmountDeviation, $"{input.AmountDeviation:F1}× above customer average"));
        if (input.CustomerAgeDays < 7)
            explanations.Add(("Maturity", input.CustomerAgeDays, "new customer (<7 days)"));
        if (input.FlagRate > 0.3)
            explanations.Add(("History", input.FlagRate, $"{input.FlagRate:P0} flag rate"));
        if (input.RejectRate > 0.1)
            explanations.Add(("History", input.RejectRate, $"{input.RejectRate:P0} rejection rate"));
        if (input.IsRoundAmount > 0 && input.Amount >= 1000)
            explanations.Add(("Pattern", input.Amount, "round amount (structuring indicator)"));
        if (input.IsCrossBorder > 0)
            explanations.Add(("Geography", 1f, "cross-border transaction"));
        if (input.KycLevel < 1)
            explanations.Add(("KYC", input.KycLevel, "unverified KYC"));

        if (explanations.Count == 0)
        {
            return probability > 0.5f
                ? "Multiple subtle risk patterns detected"
                : "No significant risk indicators";
        }

        // Take top 3 most relevant
        var top = explanations.Take(3).Select(e => e.Reason);
        return string.Join("; ", top);
    }

    /// <summary>
    /// Cached model data — immutable after creation.
    /// </summary>
    private sealed record CachedModel(MLContext Context, ITransformer Model, string Version);
}
