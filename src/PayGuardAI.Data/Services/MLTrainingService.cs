using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;
using PayGuardAI.Data.ML;

namespace PayGuardAI.Data.Services;

/// <summary>
/// ML training service — builds binary classification models from HITL-labeled data.
/// Uses ML.NET FastTree (gradient-boosted decision trees) as the default trainer.
///
/// Training flow:
///   1. Query RiskAnalysis records with ReviewStatus = Approved or Rejected
///   2. Join with Transaction + CustomerProfile for features
///   3. Compute velocity features for each training sample
///   4. Build ML.NET pipeline (Concatenate → Normalize → FastTree)
///   5. Train with 5-fold cross-validation for metrics
///   6. Train final model on full data
///   7. Serialize and persist to MLModel table
///   8. Activate new model, evict old cache
/// </summary>
public class MLTrainingService : IMLTrainingService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly IMLScoringService _mlScoringService;
    private readonly ILogger<MLTrainingService> _logger;

    // Minimum thresholds for training
    private const int MinTotalSamples = 50;
    private const int MinPerClass = 5;

    // Feature column names (must match TransactionMLInput property names)
    private static readonly string[] FeatureColumns =
    [
        "Amount", "AmountLog", "IsRoundAmount",
        "HourOfDay", "DayOfWeek", "IsWeekend", "IsNightTime",
        "IsSend", "IsReceive", "IsDeposit", "IsWithdraw",
        "IsCrossBorder", "IsCrossCurrency", "IsHighRiskCountry",
        "CustomerAgeDays", "TotalTransactions", "TotalVolume",
        "AverageAmount", "MaxAmount", "AmountDeviation",
        "KycLevel", "RiskTier", "FlagRate", "RejectRate",
        "Velocity24h", "Volume24h"
    ];

    public MLTrainingService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IMLScoringService mlScoringService,
        ILogger<MLTrainingService> logger)
    {
        _dbFactory = dbFactory;
        _mlScoringService = mlScoringService;
        _logger = logger;
    }

    public async Task<MLTrainingResult> TrainModelAsync(
        string tenantId,
        string trainedBy,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting ML model training for tenant {TenantId} (initiated by {TrainedBy})", tenantId, trainedBy);

        try
        {
            // ── Step 1: Check training data readiness ────────────────────
            var stats = await GetTrainingDataStatsAsync(tenantId, cancellationToken);
            if (!stats.IsReadyForTraining)
            {
                return new MLTrainingResult
                {
                    Success = false,
                    ErrorMessage = $"Insufficient training data: {stats.TotalLabeled}/{stats.MinimumRequired} labeled samples " +
                                   $"({stats.ApprovedCount} approved, {stats.RejectedCount} rejected). " +
                                   $"Need at least {stats.MinimumPerClass} of each class."
                };
            }

            // ── Step 2: Extract training data ────────────────────────────
            var trainingData = await ExtractTrainingDataAsync(tenantId, cancellationToken);
            if (trainingData.Count == 0)
            {
                return new MLTrainingResult
                {
                    Success = false,
                    ErrorMessage = "No training data could be extracted. Check data integrity."
                };
            }

            int fraudCount = trainingData.Count(d => d.Label);
            int legitCount = trainingData.Count - fraudCount;

            _logger.LogInformation(
                "Extracted {Total} training samples ({Fraud} fraud, {Legit} legit) for tenant {TenantId}",
                trainingData.Count, fraudCount, legitCount, tenantId);

            // ── Step 3: Build and train the ML.NET pipeline ──────────────
            var mlContext = new MLContext(seed: 42);

            // Load data into ML.NET IDataView
            var dataView = mlContext.Data.LoadFromEnumerable(trainingData);

            // Build pipeline: Concatenate features → Normalize → FastTree
            var pipeline = mlContext.Transforms.Concatenate("Features", FeatureColumns)
                .Append(mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(mlContext.BinaryClassification.Trainers.FastTree(
                    labelColumnName: "Label",
                    featureColumnName: "Features",
                    numberOfLeaves: 20,
                    numberOfTrees: 100,
                    minimumExampleCountPerLeaf: 3,
                    learningRate: 0.1));

            // ── Step 4: Cross-validate for metrics ───────────────────────
            _logger.LogInformation("Running 5-fold cross-validation for tenant {TenantId}...", tenantId);

            var cvResults = mlContext.BinaryClassification.CrossValidate(
                dataView, pipeline, numberOfFolds: 5, labelColumnName: "Label");

            // Average metrics across folds
            double avgAccuracy = cvResults.Average(r => r.Metrics.Accuracy);
            double avgAuc = cvResults.Average(r => r.Metrics.AreaUnderRocCurve);
            double avgF1 = cvResults.Average(r => r.Metrics.F1Score);
            double avgPrecision = cvResults.Average(r => r.Metrics.PositivePrecision);
            double avgRecall = cvResults.Average(r => r.Metrics.PositiveRecall);

            _logger.LogInformation(
                "CV metrics for tenant {TenantId}: Accuracy={Accuracy:F3}, AUC={AUC:F3}, F1={F1:F3}, Precision={Precision:F3}, Recall={Recall:F3}",
                tenantId, avgAccuracy, avgAuc, avgF1, avgPrecision, avgRecall);

            // ── Step 5: Train final model on all data ────────────────────
            var finalModel = pipeline.Fit(dataView);

            // Serialize model to byte[]
            byte[] modelBytes;
            using (var memStream = new MemoryStream())
            {
                mlContext.Model.Save(finalModel, dataView.Schema, memStream);
                modelBytes = memStream.ToArray();
            }

            _logger.LogInformation("Model serialized: {Size:N0} bytes for tenant {TenantId}", modelBytes.Length, tenantId);

            // ── Step 6: Persist model and activate ───────────────────────
            await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
            context.SetTenantId(tenantId);

            // Determine next version number
            var existingCount = await context.Set<MLModel>()
                .CountAsync(cancellationToken);
            string version = $"v{existingCount + 1}";

            // Deactivate any currently active model
            var activeModels = await context.Set<MLModel>()
                .Where(m => m.IsActive)
                .ToListAsync(cancellationToken);
            foreach (var m in activeModels)
            {
                m.IsActive = false;
            }

            // Create new model record
            var mlModel = new MLModel
            {
                TenantId = tenantId,
                Version = version,
                TrainerName = "FastTree",
                TrainingSamples = trainingData.Count,
                FraudSamples = fraudCount,
                LegitSamples = legitCount,
                Accuracy = avgAccuracy,
                AUC = avgAuc,
                F1Score = avgF1,
                PositivePrecision = avgPrecision,
                PositiveRecall = avgRecall,
                IsActive = true,
                ModelData = modelBytes,
                TrainedAt = DateTime.UtcNow,
                TrainedBy = trainedBy
            };

            context.Set<MLModel>().Add(mlModel);
            await context.SaveChangesAsync(cancellationToken);

            // ── Step 7: Reload model into scoring cache ──────────────────
            _mlScoringService.EvictModel(tenantId);
            await _mlScoringService.LoadModelAsync(tenantId, cancellationToken);

            _logger.LogInformation(
                "ML model {Version} trained and activated for tenant {TenantId} — AUC={AUC:F3}",
                version, tenantId, avgAuc);

            return new MLTrainingResult
            {
                Success = true,
                ModelId = mlModel.Id,
                Version = version,
                Accuracy = avgAccuracy,
                AUC = avgAuc,
                F1Score = avgF1,
                Precision = avgPrecision,
                Recall = avgRecall,
                TrainingSamples = trainingData.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ML model training failed for tenant {TenantId}", tenantId);
            return new MLTrainingResult
            {
                Success = false,
                ErrorMessage = $"Training failed: {ex.Message}"
            };
        }
    }

    public async Task<MLModelInfo?> GetActiveModelInfoAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        context.SetTenantId(tenantId);

        var model = await context.Set<MLModel>()
            .Where(m => m.IsActive)
            .OrderByDescending(m => m.TrainedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return model != null ? MapToInfo(model) : null;
    }

    public async Task<List<MLModelInfo>> GetAllModelsAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        context.SetTenantId(tenantId);

        var models = await context.Set<MLModel>()
            .OrderByDescending(m => m.TrainedAt)
            .Select(m => new MLModelInfo
            {
                Id = m.Id,
                Version = m.Version,
                TrainerName = m.TrainerName,
                TrainingSamples = m.TrainingSamples,
                FraudSamples = m.FraudSamples,
                LegitSamples = m.LegitSamples,
                Accuracy = m.Accuracy,
                AUC = m.AUC,
                F1Score = m.F1Score,
                Precision = m.PositivePrecision,
                Recall = m.PositiveRecall,
                IsActive = m.IsActive,
                TrainedAt = m.TrainedAt,
                TrainedBy = m.TrainedBy,
                Notes = m.Notes
            })
            .ToListAsync(cancellationToken);

        return models;
    }

    public async Task<MLTrainingDataStats> GetTrainingDataStatsAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        context.SetTenantId(tenantId);

        var approvedCount = await context.RiskAnalyses
            .CountAsync(r => r.ReviewStatus == ReviewStatus.Approved, cancellationToken);
        var rejectedCount = await context.RiskAnalyses
            .CountAsync(r => r.ReviewStatus == ReviewStatus.Rejected, cancellationToken);

        return new MLTrainingDataStats
        {
            TotalLabeled = approvedCount + rejectedCount,
            ApprovedCount = approvedCount,
            RejectedCount = rejectedCount,
            MinimumRequired = MinTotalSamples,
            MinimumPerClass = MinPerClass
        };
    }

    public async Task ActivateModelAsync(Guid modelId, string tenantId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        context.SetTenantId(tenantId);

        // Deactivate all
        var allModels = await context.Set<MLModel>().ToListAsync(cancellationToken);
        foreach (var m in allModels)
        {
            m.IsActive = m.Id == modelId;
        }
        await context.SaveChangesAsync(cancellationToken);

        // Reload cache
        _mlScoringService.EvictModel(tenantId);
        await _mlScoringService.LoadModelAsync(tenantId, cancellationToken);

        _logger.LogInformation("Activated ML model {ModelId} for tenant {TenantId}", modelId, tenantId);
    }

    public async Task DeactivateModelAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        context.SetTenantId(tenantId);

        var activeModels = await context.Set<MLModel>()
            .Where(m => m.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var m in activeModels)
        {
            m.IsActive = false;
        }
        await context.SaveChangesAsync(cancellationToken);

        _mlScoringService.EvictModel(tenantId);

        _logger.LogInformation("Deactivated all ML models for tenant {TenantId}", tenantId);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Private helpers
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extract training data: join RiskAnalysis (labels) → Transaction → CustomerProfile,
    /// compute velocity for each sample, and build feature vectors.
    /// </summary>
    private async Task<List<TransactionMLInput>> ExtractTrainingDataAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        context.SetTenantId(tenantId);

        // Get labeled risk analyses with their transactions
        var labeledAnalyses = await context.RiskAnalyses
            .Include(r => r.Transaction)
            .Where(r => r.ReviewStatus == ReviewStatus.Approved || r.ReviewStatus == ReviewStatus.Rejected)
            .ToListAsync(cancellationToken);

        // Load all customer profiles for batch lookup
        var senderIds = labeledAnalyses.Select(a => a.Transaction.SenderId).Distinct().ToList();
        var profiles = await context.CustomerProfiles
            .Where(p => senderIds.Contains(p.ExternalId))
            .ToDictionaryAsync(p => p.ExternalId, cancellationToken);

        // Build feature vectors
        var trainingData = new List<TransactionMLInput>();

        foreach (var analysis in labeledAnalyses)
        {
            var transaction = analysis.Transaction;
            if (transaction == null) continue;

            // Get or create a placeholder profile
            if (!profiles.TryGetValue(transaction.SenderId, out var profile))
            {
                profile = new CustomerProfile
                {
                    ExternalId = transaction.SenderId,
                    TenantId = tenantId,
                    TotalTransactions = 0
                };
            }

            // Compute velocity for this historical transaction
            var cutoff24h = transaction.CreatedAt.AddHours(-24);
            var velocity24h = await context.Transactions
                .CountAsync(t => t.SenderId == transaction.SenderId
                    && t.CreatedAt >= cutoff24h
                    && t.CreatedAt < transaction.CreatedAt, cancellationToken);
            var volume24h = await context.Transactions
                .Where(t => t.SenderId == transaction.SenderId
                    && t.CreatedAt >= cutoff24h
                    && t.CreatedAt < transaction.CreatedAt)
                .SumAsync(t => (decimal?)t.Amount ?? 0, cancellationToken);

            // Label: Rejected = fraud (true), Approved = legitimate (false)
            bool isFraud = analysis.ReviewStatus == ReviewStatus.Rejected;

            var features = MLFeatureExtractor.ExtractFeatures(
                transaction, profile, velocity24h, volume24h, label: isFraud);

            trainingData.Add(features);
        }

        return trainingData;
    }

    private static MLModelInfo MapToInfo(MLModel model) => new()
    {
        Id = model.Id,
        Version = model.Version,
        TrainerName = model.TrainerName,
        TrainingSamples = model.TrainingSamples,
        FraudSamples = model.FraudSamples,
        LegitSamples = model.LegitSamples,
        Accuracy = model.Accuracy,
        AUC = model.AUC,
        F1Score = model.F1Score,
        Precision = model.PositivePrecision,
        Recall = model.PositiveRecall,
        IsActive = model.IsActive,
        TrainedAt = model.TrainedAt,
        TrainedBy = model.TrainedBy,
        Notes = model.Notes
    };
}
