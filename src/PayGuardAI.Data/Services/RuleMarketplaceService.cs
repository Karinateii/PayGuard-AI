using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Rule Marketplace service — browse, import, and analyze rule templates.
///
/// Templates are global (no TenantId, no query filter). When a template is
/// imported, it creates or updates a tenant-scoped <see cref="RiskRule"/>.
///
/// Analytics are computed from existing <see cref="RiskFactor"/> records
/// (which rules fired on each transaction) joined with <see cref="RiskAnalysis"/>
/// review outcomes (Approved = false positive, Rejected = true positive).
/// </summary>
public class RuleMarketplaceService : IRuleMarketplaceService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RuleMarketplaceService> _logger;

    public RuleMarketplaceService(
        ApplicationDbContext context,
        ILogger<RuleMarketplaceService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Browse templates
    // ═══════════════════════════════════════════════════════════════════

    public async Task<List<RuleTemplateInfo>> GetTemplatesAsync(
        string tenantId,
        RuleTemplateFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        // Templates have no query filter (global catalog)
        IQueryable<RuleTemplate> query = _context.RuleTemplates;

        // Apply filters
        if (!string.IsNullOrEmpty(filter?.Industry))
        {
            query = query.Where(t => t.Industry == filter.Industry);
        }

        if (!string.IsNullOrEmpty(filter?.Category))
        {
            query = query.Where(t => t.Category == filter.Category);
        }

        if (!string.IsNullOrEmpty(filter?.SearchTerm))
        {
            var term = filter.SearchTerm.ToLower();
            query = query.Where(t =>
                t.Name.ToLower().Contains(term) ||
                t.Description.ToLower().Contains(term));
        }

        var templates = await query
            .OrderBy(t => t.Industry)
            .ThenBy(t => t.Category)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);

        // Get the tenant's OWN rule codes to mark "Already Imported".
        // Explicitly filter by TenantId to exclude global template rules
        // (the query filter includes TenantId == "" which are shared defaults).
        var tenantRuleCodes = await _context.RiskRules
            .Where(r => r.TenantId == tenantId)
            .Select(r => r.RuleCode)
            .ToListAsync(cancellationToken);

        var tenantRuleCodeSet = new HashSet<string>(tenantRuleCodes);

        return templates.Select(t => MapToInfo(t, tenantRuleCodeSet)).ToList();
    }

    public async Task<RuleTemplateInfo?> GetTemplateByIdAsync(
        Guid templateId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var template = await _context.RuleTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);

        if (template == null) return null;

        var tenantRuleCodes = await _context.RiskRules
            .Where(r => r.TenantId == tenantId)
            .Select(r => r.RuleCode)
            .ToListAsync(cancellationToken);

        return MapToInfo(template, new HashSet<string>(tenantRuleCodes));
    }

    // ═══════════════════════════════════════════════════════════════════
    // Import templates
    // ═══════════════════════════════════════════════════════════════════

    public async Task<RuleImportResult> ImportTemplateAsync(
        Guid templateId,
        string tenantId,
        string importedBy,
        CancellationToken cancellationToken = default)
    {
        var template = await _context.RuleTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);

        if (template == null)
        {
            return new RuleImportResult
            {
                Success = false,
                Action = ImportAction.Skipped,
                ErrorMessage = "Template not found"
            };
        }

        var result = await ImportSingleTemplateAsync(template, tenantId, importedBy, cancellationToken);

        // Increment import count on the template (popularity tracking)
        if (result.Success)
        {
            template.ImportCount++;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    public async Task<RuleBulkImportResult> ImportIndustryPackAsync(
        string industry,
        string tenantId,
        string importedBy,
        CancellationToken cancellationToken = default)
    {
        var templates = await _context.RuleTemplates
            .Where(t => t.Industry == industry)
            .ToListAsync(cancellationToken);

        if (templates.Count == 0)
        {
            return new RuleBulkImportResult
            {
                Success = false,
                Industry = industry,
                Results = [new RuleImportResult
                {
                    Success = false,
                    Action = ImportAction.Skipped,
                    ErrorMessage = $"No templates found for industry '{industry}'"
                }]
            };
        }

        var results = new List<RuleImportResult>();
        int created = 0, updated = 0, skipped = 0;

        foreach (var template in templates)
        {
            var result = await ImportSingleTemplateAsync(template, tenantId, importedBy, cancellationToken);
            results.Add(result);

            switch (result.Action)
            {
                case ImportAction.Created: created++; break;
                case ImportAction.Updated: updated++; break;
                case ImportAction.Skipped: skipped++; break;
            }

            if (result.Success)
            {
                template.ImportCount++;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Industry pack '{Industry}' imported for tenant {TenantId}: {Created} created, {Updated} updated, {Skipped} skipped",
            industry, tenantId, created, updated, skipped);

        return new RuleBulkImportResult
        {
            Success = true,
            Industry = industry,
            Created = created,
            Updated = updated,
            Skipped = skipped,
            Results = results
        };
    }

    /// <summary>
    /// Import a single template as a tenant rule: create if missing, update if exists.
    /// </summary>
    private async Task<RuleImportResult> ImportSingleTemplateAsync(
        RuleTemplate template,
        string tenantId,
        string importedBy,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if the tenant already has their OWN rule with this RuleCode.
            // Explicitly filter by TenantId to avoid accidentally updating
            // global template rules (TenantId == "") shared across all tenants.
            var existingRule = await _context.RiskRules
                .Where(r => r.TenantId == tenantId)
                .FirstOrDefaultAsync(r => r.RuleCode == template.RuleCode, cancellationToken);

            if (existingRule != null)
            {
                // Update existing rule with template's recommended parameters
                existingRule.Threshold = template.Threshold;
                existingRule.ScoreWeight = template.ScoreWeight;
                existingRule.Description = template.Description;
                existingRule.UpdatedAt = DateTime.UtcNow;
                existingRule.UpdatedBy = importedBy;

                _logger.LogInformation(
                    "Updated rule {RuleCode} for tenant {TenantId} from template '{TemplateName}'",
                    template.RuleCode, tenantId, template.Name);

                return new RuleImportResult
                {
                    Success = true,
                    RuleCode = template.RuleCode,
                    TemplateName = template.Name,
                    Action = ImportAction.Updated
                };
            }

            // Create new tenant-scoped rule from template
            _context.RiskRules.Add(new RiskRule
            {
                TenantId = tenantId,
                RuleCode = template.RuleCode,
                Name = template.Name,
                Description = template.Description,
                Category = template.Category,
                Threshold = template.Threshold,
                ScoreWeight = template.ScoreWeight,
                IsEnabled = true,
                UpdatedBy = importedBy
            });

            _logger.LogInformation(
                "Created rule {RuleCode} for tenant {TenantId} from template '{TemplateName}'",
                template.RuleCode, tenantId, template.Name);

            return new RuleImportResult
            {
                Success = true,
                RuleCode = template.RuleCode,
                TemplateName = template.Name,
                Action = ImportAction.Created
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to import template '{TemplateName}' for tenant {TenantId}",
                template.Name, tenantId);

            return new RuleImportResult
            {
                Success = false,
                RuleCode = template.RuleCode,
                TemplateName = template.Name,
                Action = ImportAction.Skipped,
                ErrorMessage = ex.Message
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Rule Analytics
    // ═══════════════════════════════════════════════════════════════════

    public async Task<List<RuleAnalyticsInfo>> GetRuleAnalyticsAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        // Get all rules for the tenant (tenant query filter auto-applies)
        var rules = await _context.RiskRules
            .OrderBy(r => r.Category)
            .ThenBy(r => r.Name)
            .ToListAsync(cancellationToken);

        if (rules.Count == 0) return [];

        // Total transactions for hit rate denominator
        var totalTransactions = await _context.Transactions
            .CountAsync(cancellationToken);

        // Get hit counts per rule name (exclude ML factor — it's not a rule)
        var hitCounts = await _context.RiskFactors
            .Where(rf => rf.Category != "ML")
            .GroupBy(rf => rf.RuleName)
            .Select(g => new { RuleName = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var hitMap = hitCounts.ToDictionary(h => h.RuleName, h => h.Count);

        // Get reviewed outcomes per rule name for precision calculation.
        // Join RiskFactor → RiskAnalysis to correlate rule hits with review decisions.
        // Rejected = true positive (rule correctly flagged fraud).
        // Approved = false positive (rule flagged a legitimate transaction).
        var reviewedHits = await _context.RiskFactors
            .Where(rf => rf.Category != "ML")
            .Join(
                _context.RiskAnalyses.Where(ra =>
                    ra.ReviewStatus == ReviewStatus.Approved ||
                    ra.ReviewStatus == ReviewStatus.Rejected),
                rf => rf.RiskAnalysisId,
                ra => ra.Id,
                (rf, ra) => new { rf.RuleName, ra.ReviewStatus })
            .GroupBy(x => x.RuleName)
            .Select(g => new
            {
                RuleName = g.Key,
                TruePositives = g.Count(x => x.ReviewStatus == ReviewStatus.Rejected),
                FalsePositives = g.Count(x => x.ReviewStatus == ReviewStatus.Approved)
            })
            .ToListAsync(cancellationToken);

        var reviewMap = reviewedHits.ToDictionary(r => r.RuleName);

        // Build analytics for each rule
        var analytics = new List<RuleAnalyticsInfo>();

        foreach (var rule in rules)
        {
            int totalHits = hitMap.GetValueOrDefault(rule.Name, 0);
            int tp = 0, fp = 0;

            if (reviewMap.TryGetValue(rule.Name, out var reviewed))
            {
                tp = reviewed.TruePositives;
                fp = reviewed.FalsePositives;
            }

            int totalReviewed = tp + fp;

            analytics.Add(new RuleAnalyticsInfo
            {
                RuleCode = rule.RuleCode,
                RuleName = rule.Name,
                Category = rule.Category,
                IsEnabled = rule.IsEnabled,
                TotalHits = totalHits,
                HitRatePercent = totalTransactions > 0
                    ? Math.Round((double)totalHits / totalTransactions * 100, 1)
                    : 0,
                TruePositives = tp,
                FalsePositives = fp,
                PrecisionPercent = totalReviewed > 0
                    ? Math.Round((double)tp / totalReviewed * 100, 1)
                    : 0,
                FalsePositiveRatePercent = totalReviewed > 0
                    ? Math.Round((double)fp / totalReviewed * 100, 1)
                    : 0
            });
        }

        return analytics.OrderByDescending(a => a.TotalHits).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════

    public async Task<List<string>> GetIndustriesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.RuleTemplates
            .Select(t => t.Industry)
            .Distinct()
            .OrderBy(i => i)
            .ToListAsync(cancellationToken);
    }

    private static RuleTemplateInfo MapToInfo(RuleTemplate template, HashSet<string> tenantRuleCodes) => new()
    {
        Id = template.Id,
        Name = template.Name,
        Description = template.Description,
        RuleCode = template.RuleCode,
        Category = template.Category,
        Threshold = template.Threshold,
        ScoreWeight = template.ScoreWeight,
        Industry = template.Industry,
        Tags = template.Tags,
        IsBuiltIn = template.IsBuiltIn,
        ImportCount = template.ImportCount,
        Author = template.Author,
        Version = template.Version,
        IsImported = tenantRuleCodes.Contains(template.RuleCode)
    };
}
