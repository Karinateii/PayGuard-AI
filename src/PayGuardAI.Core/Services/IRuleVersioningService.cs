using PayGuardAI.Core.Entities;

namespace PayGuardAI.Core.Services;

/// <summary>
/// Service for snapshotting rule configurations, listing version history,
/// and restoring (rolling back) rules to a previous version.
/// </summary>
public interface IRuleVersioningService
{
    /// <summary>
    /// Take a snapshot of a RiskRule's current config BEFORE applying changes.
    /// </summary>
    Task<RuleVersion> SnapshotRuleAsync(RiskRule rule, string changedBy, string changeDescription, CancellationToken ct = default);

    /// <summary>
    /// Take a snapshot of a RuleGroup's current config BEFORE applying changes.
    /// </summary>
    Task<RuleVersion> SnapshotCompoundRuleAsync(RuleGroup group, string changedBy, string changeDescription, CancellationToken ct = default);

    /// <summary>
    /// Get all versions of a specific rule, newest first.
    /// </summary>
    Task<List<RuleVersion>> GetVersionHistoryAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>
    /// Rollback a RiskRule to a specific version snapshot.
    /// Returns the restored rule.
    /// </summary>
    Task<RiskRule?> RollbackRuleAsync(Guid versionId, string rolledBackBy, CancellationToken ct = default);

    /// <summary>
    /// Rollback a RuleGroup to a specific version snapshot.
    /// Returns the restored group.
    /// </summary>
    Task<RuleGroup?> RollbackCompoundRuleAsync(Guid versionId, string rolledBackBy, CancellationToken ct = default);

    /// <summary>
    /// Get a single version by ID (for preview before rollback).
    /// </summary>
    Task<RuleVersion?> GetVersionAsync(Guid versionId, CancellationToken ct = default);

    /// <summary>
    /// Count how many versions exist for a specific rule.
    /// </summary>
    Task<int> GetVersionCountAsync(Guid entityId, CancellationToken ct = default);
}
