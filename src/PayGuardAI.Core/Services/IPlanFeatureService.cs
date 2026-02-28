using PayGuardAI.Core.Entities;

namespace PayGuardAI.Core.Services;

/// <summary>
/// Defines what each subscription plan can access.
/// Used by Blazor pages and services to gate features based on the tenant's current plan.
/// </summary>
public interface IPlanFeatureService
{
    /// <summary>Get the tenant's current billing plan. Returns Trial if no subscription exists.</summary>
    Task<BillingPlan> GetCurrentPlanAsync(string tenantId, CancellationToken ct = default);

    /// <summary>Check whether a specific feature is available on the given plan.</summary>
    bool IsFeatureAvailable(BillingPlan plan, PlanFeature feature);

    /// <summary>Get the maximum number of team members allowed on a plan.</summary>
    int GetMaxTeamMembers(BillingPlan plan);

    /// <summary>Get the maximum number of API keys allowed on a plan.</summary>
    int GetMaxApiKeys(BillingPlan plan);

    /// <summary>Get the maximum number of webhook endpoints allowed on a plan.</summary>
    int GetMaxWebhookEndpoints(BillingPlan plan);

    /// <summary>Get the transaction limit for a plan (per billing period).</summary>
    int GetTransactionLimit(BillingPlan plan);

    /// <summary>Get the minimum plan required for a feature.</summary>
    BillingPlan GetMinimumPlan(PlanFeature feature);

    /// <summary>Get a user-friendly name for a plan.</summary>
    string GetPlanDisplayName(BillingPlan plan);
}

/// <summary>
/// Enumeration of gated features in PayGuard AI.
/// Each feature maps to a minimum subscription plan.
/// </summary>
public enum PlanFeature
{
    // ── Starter (included in all paid plans) ──
    BasicRiskScoring,
    BuiltInRules,
    HitlReviewQueue,
    TransactionDashboard,
    BasicReports,
    EmailAlerts,
    ManualWatchlists,
    CustomerProfiles,
    AuditTrail,
    BasicApiAccess,

    // ── Pro ──
    CustomRules,
    CompoundRules,
    RuleMarketplace,
    MLRiskScoring,
    AdvancedAnalytics,
    ScheduledReports,
    NetworkAnalysis,
    OutboundWebhooks,
    SlackAlerts,
    WatchlistCsvImport,
    CustomRoles,
    RuleSuggestions,

    // ── Enterprise ──
    GdprCompliance,
    UnlimitedTransactions,
}
