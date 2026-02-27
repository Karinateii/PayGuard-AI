using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Implements rule versioning: snapshot before every edit, list history, one-click rollback.
///
/// Snapshot strategy:
///   - Caller invokes SnapshotRuleAsync() BEFORE saving changes.
///   - The current state is serialized to JSON and stored as a RuleVersion row.
///   - Version numbers auto-increment per rule.
///
/// Rollback strategy:
///   - Deserialize the JSON snapshot back into the entity.
///   - Overwrite the current rule properties with the snapshot values.
///   - A new snapshot is taken of the pre-rollback state so nothing is lost.
/// </summary>
public class RuleVersioningService : IRuleVersioningService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<RuleVersioningService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public RuleVersioningService(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ILogger<RuleVersioningService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    // ── Snapshot ───────────────────────────────────────────────────

    public async Task<RuleVersion> SnapshotRuleAsync(RiskRule rule, string changedBy, string changeDescription, CancellationToken ct = default)
    {
        var nextVersion = await GetNextVersionNumberAsync(rule.Id, ct);

        var snapshot = new RuleVersionSnapshot
        {
            Name = rule.Name,
            Description = rule.Description,
            Category = rule.Category,
            RuleCode = rule.RuleCode,
            Threshold = rule.Threshold,
            ScoreWeight = rule.ScoreWeight,
            Mode = rule.Mode,
            ExpressionField = rule.ExpressionField,
            ExpressionOperator = rule.ExpressionOperator,
            ExpressionValue = rule.ExpressionValue
        };

        // Use rule.TenantId (not _tenantContext.TenantId) to guarantee the version
        // matches the rule's actual tenant — avoids mismatches when the ambient
        // tenant context hasn't been set yet (e.g., marketplace import during SSR).
        var version = new RuleVersion
        {
            TenantId = rule.TenantId,
            EntityType = "RiskRule",
            EntityId = rule.Id,
            VersionNumber = nextVersion,
            ConfigJson = JsonSerializer.Serialize(snapshot, JsonOpts),
            ChangeDescription = changeDescription,
            ChangedBy = changedBy,
            RuleName = rule.Name,
            CreatedAt = DateTime.UtcNow
        };

        _context.RuleVersions.Add(version);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Snapshot v{Version} for RiskRule '{RuleName}' ({RuleId}) tenant={TenantId} by {User}",
            nextVersion, rule.Name, rule.Id, rule.TenantId, changedBy);

        return version;
    }

    public async Task<RuleVersion> SnapshotCompoundRuleAsync(RuleGroup group, string changedBy, string changeDescription, CancellationToken ct = default)
    {
        var nextVersion = await GetNextVersionNumberAsync(group.Id, ct);

        var snapshot = new CompoundRuleVersionSnapshot
        {
            Name = group.Name,
            Description = group.Description,
            Category = group.Category,
            LogicalOperator = group.LogicalOperator,
            RiskPoints = group.RiskPoints,
            Mode = group.Mode,
            Conditions = group.Conditions.OrderBy(c => c.OrderIndex).Select(c => new ConditionSnapshot
            {
                ExpressionField = c.ExpressionField,
                ExpressionOperator = c.ExpressionOperator,
                ExpressionValue = c.ExpressionValue,
                OrderIndex = c.OrderIndex
            }).ToList()
        };

        // Use group.TenantId (not _tenantContext.TenantId) — same rationale as SnapshotRuleAsync
        var version = new RuleVersion
        {
            TenantId = group.TenantId,
            EntityType = "RuleGroup",
            EntityId = group.Id,
            VersionNumber = nextVersion,
            ConfigJson = JsonSerializer.Serialize(snapshot, JsonOpts),
            ChangeDescription = changeDescription,
            ChangedBy = changedBy,
            RuleName = group.Name,
            CreatedAt = DateTime.UtcNow
        };

        _context.RuleVersions.Add(version);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Snapshot v{Version} for RuleGroup '{GroupName}' ({GroupId}) tenant={TenantId} by {User}",
            nextVersion, group.Name, group.Id, group.TenantId, changedBy);

        return version;
    }

    // ── History & Lookup ──────────────────────────────────────────

    public async Task<List<RuleVersion>> GetVersionHistoryAsync(Guid entityId, CancellationToken ct = default)
    {
        return await _context.RuleVersions
            .Where(v => v.EntityId == entityId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct);
    }

    public async Task<RuleVersion?> GetVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        return await _context.RuleVersions.FindAsync([versionId], ct);
    }

    public async Task<int> GetVersionCountAsync(Guid entityId, CancellationToken ct = default)
    {
        return await _context.RuleVersions.CountAsync(v => v.EntityId == entityId, ct);
    }

    // ── Rollback ──────────────────────────────────────────────────

    public async Task<RiskRule?> RollbackRuleAsync(Guid versionId, string rolledBackBy, CancellationToken ct = default)
    {
        var version = await _context.RuleVersions.FindAsync([versionId], ct);
        if (version is null || version.EntityType != "RiskRule")
        {
            _logger.LogWarning("Rollback failed: version {VersionId} not found or not a RiskRule", versionId);
            return null;
        }

        var rule = await _context.RiskRules.FindAsync([version.EntityId], ct);
        if (rule is null)
        {
            _logger.LogWarning("Rollback failed: RiskRule {RuleId} not found", version.EntityId);
            return null;
        }

        // Snapshot the CURRENT state before overwriting (so the pre-rollback state isn't lost)
        await SnapshotRuleAsync(rule, rolledBackBy, $"Auto-snapshot before rollback to v{version.VersionNumber}", ct);

        // Deserialize the snapshot
        var snapshot = JsonSerializer.Deserialize<RuleVersionSnapshot>(version.ConfigJson, JsonOpts);
        if (snapshot is null)
        {
            _logger.LogWarning("Rollback failed: could not deserialize snapshot for version {VersionId}", versionId);
            return null;
        }

        // Apply the snapshot values
        rule.Name = snapshot.Name;
        rule.Description = snapshot.Description;
        rule.Category = snapshot.Category;
        rule.Threshold = snapshot.Threshold;
        rule.ScoreWeight = snapshot.ScoreWeight;
        rule.Mode = snapshot.Mode;
        rule.ExpressionField = snapshot.ExpressionField ?? string.Empty;
        rule.ExpressionOperator = snapshot.ExpressionOperator ?? string.Empty;
        rule.ExpressionValue = snapshot.ExpressionValue ?? string.Empty;
        rule.UpdatedAt = DateTime.UtcNow;
        rule.UpdatedBy = rolledBackBy;

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Rolled back RiskRule '{RuleName}' ({RuleId}) to v{Version} by {User}",
            rule.Name, rule.Id, version.VersionNumber, rolledBackBy);

        return rule;
    }

    public async Task<RuleGroup?> RollbackCompoundRuleAsync(Guid versionId, string rolledBackBy, CancellationToken ct = default)
    {
        var version = await _context.RuleVersions.FindAsync([versionId], ct);
        if (version is null || version.EntityType != "RuleGroup")
        {
            _logger.LogWarning("Rollback failed: version {VersionId} not found or not a RuleGroup", versionId);
            return null;
        }

        var group = await _context.RuleGroups
            .Include(g => g.Conditions)
            .FirstOrDefaultAsync(g => g.Id == version.EntityId, ct);
        if (group is null)
        {
            _logger.LogWarning("Rollback failed: RuleGroup {GroupId} not found", version.EntityId);
            return null;
        }

        // Snapshot current state before rollback
        await SnapshotCompoundRuleAsync(group, rolledBackBy, $"Auto-snapshot before rollback to v{version.VersionNumber}", ct);

        var snapshot = JsonSerializer.Deserialize<CompoundRuleVersionSnapshot>(version.ConfigJson, JsonOpts);
        if (snapshot is null)
        {
            _logger.LogWarning("Rollback failed: could not deserialize compound snapshot for version {VersionId}", versionId);
            return null;
        }

        // Apply snapshot values
        group.Name = snapshot.Name;
        group.Description = snapshot.Description;
        group.Category = snapshot.Category;
        group.LogicalOperator = snapshot.LogicalOperator;
        group.RiskPoints = snapshot.RiskPoints;
        group.Mode = snapshot.Mode;
        group.UpdatedAt = DateTime.UtcNow;

        // Replace conditions: remove existing, add snapshotted ones
        _context.RuleGroupConditions.RemoveRange(group.Conditions);
        group.Conditions.Clear();

        foreach (var cs in snapshot.Conditions)
        {
            group.Conditions.Add(new RuleGroupCondition
            {
                RuleGroupId = group.Id,
                ExpressionField = cs.ExpressionField,
                ExpressionOperator = cs.ExpressionOperator,
                ExpressionValue = cs.ExpressionValue,
                OrderIndex = cs.OrderIndex
            });
        }

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Rolled back RuleGroup '{GroupName}' ({GroupId}) to v{Version} by {User}",
            group.Name, group.Id, version.VersionNumber, rolledBackBy);

        return group;
    }

    // ── Helpers ────────────────────────────────────────────────────

    private async Task<int> GetNextVersionNumberAsync(Guid entityId, CancellationToken ct)
    {
        var maxVersion = await _context.RuleVersions
            .Where(v => v.EntityId == entityId)
            .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0;

        return maxVersion + 1;
    }

    // ── Snapshot DTOs (internal, serialized to JSON) ──────────────

    private class RuleVersionSnapshot
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public string RuleCode { get; set; } = "";
        public decimal Threshold { get; set; }
        public int ScoreWeight { get; set; }
        public string Mode { get; set; } = "Active";
        public string? ExpressionField { get; set; }
        public string? ExpressionOperator { get; set; }
        public string? ExpressionValue { get; set; }
    }

    private class CompoundRuleVersionSnapshot
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public string LogicalOperator { get; set; } = "AND";
        public int RiskPoints { get; set; }
        public string Mode { get; set; } = "Active";
        public List<ConditionSnapshot> Conditions { get; set; } = [];
    }

    private class ConditionSnapshot
    {
        public string ExpressionField { get; set; } = "";
        public string ExpressionOperator { get; set; } = "";
        public string ExpressionValue { get; set; } = "";
        public int OrderIndex { get; set; }
    }
}
