using PayGuardAI.Core.Entities;

namespace PayGuardAI.Core.Services;

// ═════════════════════════════════════════════════════════════════════════
// Rule Marketplace — browse, import, and analyze rule templates
// ═════════════════════════════════════════════════════════════════════════

/// <summary>
/// Service for the Rule Marketplace: browse pre-built rule templates,
/// import them into a tenant's rule set, and analyze rule effectiveness.
/// </summary>
public interface IRuleMarketplaceService
{
    /// <summary>
    /// Browse available rule templates with optional filtering.
    /// Includes an <c>IsImported</c> flag per template if the tenant
    /// already has a rule with the same RuleCode.
    /// </summary>
    Task<List<RuleTemplateInfo>> GetTemplatesAsync(
        string tenantId,
        RuleTemplateFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get details for a single template by ID.
    /// </summary>
    Task<RuleTemplateInfo?> GetTemplateByIdAsync(
        Guid templateId,
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Import a single template into a tenant's rule set.
    /// If the tenant already has a rule with the same RuleCode,
    /// the existing rule's threshold and weight are updated.
    /// </summary>
    Task<RuleImportResult> ImportTemplateAsync(
        Guid templateId,
        string tenantId,
        string importedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Import all templates for a given industry as a pack.
    /// Creates or updates tenant rules for each template in the industry.
    /// </summary>
    Task<RuleBulkImportResult> ImportIndustryPackAsync(
        string industry,
        string tenantId,
        string importedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get effectiveness analytics for the tenant's active rules.
    /// Computes hit rate, precision, and false positive rate from
    /// RiskFactor + RiskAnalysis review outcomes.
    /// </summary>
    Task<List<RuleAnalyticsInfo>> GetRuleAnalyticsAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the distinct list of industries with available templates.
    /// </summary>
    Task<List<string>> GetIndustriesAsync(CancellationToken cancellationToken = default);
}

// ═════════════════════════════════════════════════════════════════════════
// DTOs
// ═════════════════════════════════════════════════════════════════════════

/// <summary>
/// Summary of a rule template for display in the marketplace.
/// </summary>
public class RuleTemplateInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RuleCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Threshold { get; set; }
    public int ScoreWeight { get; set; }
    public string Industry { get; set; } = string.Empty;
    public string[] Tags { get; set; } = [];
    public bool IsBuiltIn { get; set; }
    public int ImportCount { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// True if the tenant already has a rule with this RuleCode.
    /// </summary>
    public bool IsImported { get; set; }
}

/// <summary>
/// Filter criteria for browsing templates.
/// </summary>
public class RuleTemplateFilter
{
    /// <summary>Filter by industry (exact match).</summary>
    public string? Industry { get; set; }

    /// <summary>Filter by category (exact match).</summary>
    public string? Category { get; set; }

    /// <summary>Free-text search across name, description, and tags.</summary>
    public string? SearchTerm { get; set; }
}

/// <summary>
/// Result of importing a single template.
/// </summary>
public class RuleImportResult
{
    public bool Success { get; set; }
    public string RuleCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>What action was taken: Created a new rule, Updated existing, or Skipped.</summary>
    public ImportAction Action { get; set; }

    public string? ErrorMessage { get; set; }
}

/// <summary>
/// What happened when importing a template.
/// </summary>
public enum ImportAction
{
    /// <summary>New rule was created from the template.</summary>
    Created,

    /// <summary>Existing rule's threshold and weight were updated.</summary>
    Updated,

    /// <summary>Import was skipped (error or constraint).</summary>
    Skipped
}

/// <summary>
/// Result of importing an entire industry pack.
/// </summary>
public class RuleBulkImportResult
{
    public bool Success { get; set; }
    public string Industry { get; set; } = string.Empty;
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<RuleImportResult> Results { get; set; } = [];
}

/// <summary>
/// Effectiveness analytics for a single rule in the tenant's rule set.
/// Computed from RiskFactor hits + RiskAnalysis review outcomes.
/// </summary>
public class RuleAnalyticsInfo
{
    public string RuleCode { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }

    /// <summary>Number of transactions this rule flagged (total all-time).</summary>
    public int TotalHits { get; set; }

    /// <summary>Percentage of all transactions this rule fires on.</summary>
    public double HitRatePercent { get; set; }

    /// <summary>Number of hits that were later Rejected (confirmed fraud).</summary>
    public int TruePositives { get; set; }

    /// <summary>Number of hits that were later Approved (false alarm).</summary>
    public int FalsePositives { get; set; }

    /// <summary>Precision: TP / (TP + FP) × 100. Higher = fewer false alarms.</summary>
    public double PrecisionPercent { get; set; }

    /// <summary>False positive rate: FP / (FP + TP) × 100. Lower = better.</summary>
    public double FalsePositiveRatePercent { get; set; }
}
