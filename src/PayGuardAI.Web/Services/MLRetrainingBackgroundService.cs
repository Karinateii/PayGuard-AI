using Microsoft.EntityFrameworkCore;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;
using PayGuardAI.Data;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Background service that automatically retrains ML models when enough new
/// HITL-labeled data has accumulated.
///
/// How it works:
///   1. Every <c>CheckIntervalMinutes</c> (default: 60 min), scans all tenants
///   2. For each tenant with an active ML model, compares current labeled-data
///      count against the sample count used in the last training
///   3. If labeled data grew by ≥ <c>DataGrowthThresholdPercent</c> (default: 20%)
///      AND enough time has passed since last training, triggers a retrain
///   4. New model is auto-activated only if its AUC ≥ the current model's AUC
///      (when <c>AutoActivateIfBetter</c> is true)
///
/// For tenants with NO existing model, retrains whenever the data meets
/// the minimum sample requirements (50 labeled, 5 per class).
///
/// Configuration (appsettings.json → "MLRetraining"):
///   - Enabled: true/false (master switch)
///   - CheckIntervalMinutes: how often to check (default 60)
///   - DataGrowthThresholdPercent: % growth to trigger retrain (default 20)
///   - MinHoursBetweenTraining: cooldown between retrains (default 24)
///   - AutoActivateIfBetter: auto-activate if AUC improves (default true)
/// </summary>
public class MLRetrainingBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<MLRetrainingBackgroundService> _logger;

    public MLRetrainingBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<MLRetrainingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Check if auto-retraining is enabled
        var enabled = _config.GetValue("MLRetraining:Enabled", true);
        if (!enabled)
        {
            _logger.LogInformation("ML auto-retraining is disabled via configuration");
            return;
        }

        // Check if ML scoring is enabled at all
        if (!_config.IsMLScoringEnabled())
        {
            _logger.LogInformation("ML scoring is disabled — auto-retraining will not run");
            return;
        }

        var checkIntervalMinutes = _config.GetValue("MLRetraining:CheckIntervalMinutes", 60);
        var interval = TimeSpan.FromMinutes(checkIntervalMinutes);

        _logger.LogInformation(
            "ML auto-retraining service started — checking every {Interval} minutes",
            checkIntervalMinutes);

        // Initial delay: let the app fully start and seed data
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndRetrainAllTenantsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during ML auto-retraining check");
            }

            await Task.Delay(interval, stoppingToken);
        }

        _logger.LogInformation("ML auto-retraining service stopped");
    }

    /// <summary>
    /// Discover all tenants and check each for retraining eligibility.
    /// </summary>
    private async Task CheckAndRetrainAllTenantsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

        // Discover all distinct tenant IDs from subscriptions + transactions
        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken);

        // Bypass tenant filter to get ALL tenants
        var tenantIds = await context.TenantSubscriptions
            .IgnoreQueryFilters()
            .Select(s => s.TenantId)
            .Union(
                context.Transactions
                    .IgnoreQueryFilters()
                    .Select(t => t.TenantId)
            )
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToListAsync(cancellationToken);

        if (tenantIds.Count == 0)
        {
            _logger.LogDebug("No tenants found — skipping ML retraining check");
            return;
        }

        _logger.LogDebug("Checking ML retraining eligibility for {Count} tenant(s)", tenantIds.Count);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                await CheckAndRetrainTenantAsync(tenantId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ML retraining check failed for tenant {TenantId}", tenantId);
            }
        }
    }

    /// <summary>
    /// Check if a specific tenant needs retraining and trigger if so.
    /// </summary>
    private async Task CheckAndRetrainTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        var growthThreshold = _config.GetValue("MLRetraining:DataGrowthThresholdPercent", 20);
        var minHoursBetween = _config.GetValue("MLRetraining:MinHoursBetweenTraining", 24);
        var autoActivate = _config.GetValue("MLRetraining:AutoActivateIfBetter", true);

        // Create a fresh scope for each tenant (scoped services like ITenantContext)
        await using var scope = _scopeFactory.CreateAsyncScope();

        // Set tenant context
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.TenantId = tenantId;

        var trainingService = scope.ServiceProvider.GetRequiredService<IMLTrainingService>();

        // Get current training data stats
        var stats = await trainingService.GetTrainingDataStatsAsync(tenantId, cancellationToken);
        if (!stats.IsReadyForTraining)
        {
            _logger.LogDebug(
                "Tenant {TenantId}: Not enough labeled data ({Total}/{Required}) — skipping",
                tenantId, stats.TotalLabeled, stats.MinimumRequired);
            return;
        }

        // Get the currently active model (if any)
        var activeModel = await trainingService.GetActiveModelInfoAsync(tenantId, cancellationToken);

        if (activeModel != null)
        {
            // Enforce cooldown period
            var hoursSinceLastTrain = (DateTime.UtcNow - activeModel.TrainedAt).TotalHours;
            if (hoursSinceLastTrain < minHoursBetween)
            {
                _logger.LogDebug(
                    "Tenant {TenantId}: Last trained {Hours:F1}h ago (cooldown: {Cooldown}h) — skipping",
                    tenantId, hoursSinceLastTrain, minHoursBetween);
                return;
            }

            // Check data growth: only retrain if labeled data grew significantly
            int previousSamples = activeModel.TrainingSamples;
            int currentSamples = stats.TotalLabeled;
            double growthPercent = previousSamples > 0
                ? ((double)(currentSamples - previousSamples) / previousSamples) * 100
                : 100; // first model → always train

            if (growthPercent < growthThreshold)
            {
                _logger.LogDebug(
                    "Tenant {TenantId}: Data growth {Growth:F1}% < threshold {Threshold}% ({Current} vs {Previous} samples) — skipping",
                    tenantId, growthPercent, growthThreshold, currentSamples, previousSamples);
                return;
            }

            _logger.LogInformation(
                "Tenant {TenantId}: Data grew {Growth:F1}% ({Previous} → {Current} samples) — triggering auto-retrain",
                tenantId, growthPercent, previousSamples, currentSamples);
        }
        else
        {
            // No model exists yet — train the first one
            _logger.LogInformation(
                "Tenant {TenantId}: No active model but {Total} labeled samples available — training first model",
                tenantId, stats.TotalLabeled);
        }

        // ── Trigger retraining ───────────────────────────────────────────
        var result = await trainingService.TrainModelAsync(tenantId, "system-auto-retrain", cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning(
                "Tenant {TenantId}: Auto-retrain failed — {Error}",
                tenantId, result.ErrorMessage);
            return;
        }

        _logger.LogInformation(
            "Tenant {TenantId}: Auto-retrained model {Version} — AUC={AUC:F3}, Accuracy={Accuracy:F3}, " +
            "F1={F1:F3}, Samples={Samples}",
            tenantId, result.Version, result.AUC, result.Accuracy, result.F1Score, result.TrainingSamples);

        // If AutoActivateIfBetter is enabled AND there was a previous model,
        // check if the new model is actually better before keeping it active.
        // (TrainModelAsync already activates the new model, so we may need to roll back.)
        if (autoActivate && activeModel != null)
        {
            if (result.AUC < activeModel.AUC && activeModel.AUC > 0)
            {
                // New model is worse — roll back to previous model
                _logger.LogWarning(
                    "Tenant {TenantId}: New model AUC ({NewAUC:F3}) < previous ({OldAUC:F3}) — rolling back to {OldVersion}",
                    tenantId, result.AUC, activeModel.AUC, activeModel.Version);

                await trainingService.ActivateModelAsync(activeModel.Id, tenantId, cancellationToken);

                // Update notes on the new model to indicate it was auto-deactivated
                if (result.ModelId.HasValue)
                {
                    await UpdateModelNotesAsync(
                        scope.ServiceProvider, tenantId, result.ModelId.Value,
                        $"Auto-deactivated: AUC {result.AUC:F3} < previous {activeModel.AUC:F3}",
                        cancellationToken);
                }
            }
            else
            {
                _logger.LogInformation(
                    "Tenant {TenantId}: New model {Version} kept active (AUC improved {OldAUC:F3} → {NewAUC:F3})",
                    tenantId, result.Version, activeModel.AUC, result.AUC);
            }
        }
    }

    /// <summary>
    /// Update the Notes field on a trained model (e.g. to record why it was deactivated).
    /// </summary>
    private static async Task UpdateModelNotesAsync(
        IServiceProvider serviceProvider,
        string tenantId,
        Guid modelId,
        string notes,
        CancellationToken cancellationToken)
    {
        var dbFactory = serviceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken);
        context.SetTenantId(tenantId);

        var model = await context.Set<MLModel>()
            .FirstOrDefaultAsync(m => m.Id == modelId, cancellationToken);

        if (model != null)
        {
            model.Notes = notes;
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
