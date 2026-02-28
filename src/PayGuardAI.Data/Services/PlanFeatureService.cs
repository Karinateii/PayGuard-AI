using Microsoft.EntityFrameworkCore;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Defines feature access, numeric limits, and plan-gating logic for each subscription tier.
///
/// Plan hierarchy:  Trial → Starter → Pro → Enterprise
///
/// Feature gates:
///   • Starter: Core fraud detection (built-in rules, HITL, basic reports, email alerts)
///   • Pro:     Custom/compound rules, ML scoring, advanced reports, webhooks, Slack, custom roles
///   • Enterprise: GDPR tools, unlimited everything, SLA, dedicated support
/// </summary>
public class PlanFeatureService : IPlanFeatureService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public PlanFeatureService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // ── Plan Lookup ───────────────────────────────────────────────────────────

    public async Task<BillingPlan> GetCurrentPlanAsync(string tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var sub = await db.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        if (sub == null) return BillingPlan.Trial;

        // If trial has expired and they haven't subscribed, keep them on Trial (restricted)
        if (sub.Status == "trialing" && sub.TrialEndsAt.HasValue && sub.TrialEndsAt.Value < DateTime.UtcNow)
            return BillingPlan.Trial;

        // Canceled subscriptions get downgraded to Trial after period ends
        if (sub.Status == "canceled" && sub.PeriodEnd < DateTime.UtcNow)
            return BillingPlan.Trial;

        return sub.Plan;
    }

    // ── Feature Gating ────────────────────────────────────────────────────────

    /// <summary>
    /// Maps each feature to its minimum required plan.
    /// </summary>
    private static readonly Dictionary<PlanFeature, BillingPlan> FeatureMinPlan = new()
    {
        // Starter features (available to all paid plans + trial)
        [PlanFeature.BasicRiskScoring]      = BillingPlan.Trial,
        [PlanFeature.BuiltInRules]          = BillingPlan.Trial,
        [PlanFeature.HitlReviewQueue]       = BillingPlan.Trial,
        [PlanFeature.TransactionDashboard]  = BillingPlan.Trial,
        [PlanFeature.BasicReports]          = BillingPlan.Trial,
        [PlanFeature.EmailAlerts]           = BillingPlan.Trial,
        [PlanFeature.ManualWatchlists]      = BillingPlan.Trial,
        [PlanFeature.CustomerProfiles]      = BillingPlan.Trial,
        [PlanFeature.AuditTrail]            = BillingPlan.Trial,
        [PlanFeature.BasicApiAccess]        = BillingPlan.Trial,

        // Pro features
        [PlanFeature.CustomRules]           = BillingPlan.Pro,
        [PlanFeature.CompoundRules]         = BillingPlan.Pro,
        [PlanFeature.RuleMarketplace]       = BillingPlan.Pro,
        [PlanFeature.MLRiskScoring]         = BillingPlan.Pro,
        [PlanFeature.AdvancedAnalytics]     = BillingPlan.Pro,
        [PlanFeature.ScheduledReports]      = BillingPlan.Pro,
        [PlanFeature.NetworkAnalysis]       = BillingPlan.Pro,
        [PlanFeature.OutboundWebhooks]      = BillingPlan.Pro,
        [PlanFeature.SlackAlerts]           = BillingPlan.Pro,
        [PlanFeature.WatchlistCsvImport]    = BillingPlan.Pro,
        [PlanFeature.CustomRoles]           = BillingPlan.Pro,
        [PlanFeature.RuleSuggestions]       = BillingPlan.Pro,

        // Enterprise features
        [PlanFeature.GdprCompliance]        = BillingPlan.Enterprise,
        [PlanFeature.UnlimitedTransactions] = BillingPlan.Enterprise,
        [PlanFeature.CustomIntegrations]    = BillingPlan.Enterprise,
        [PlanFeature.DedicatedSupport]      = BillingPlan.Enterprise,
        [PlanFeature.SlaGuarantee]          = BillingPlan.Enterprise,
    };

    public bool IsFeatureAvailable(BillingPlan plan, PlanFeature feature)
    {
        var minPlan = FeatureMinPlan.GetValueOrDefault(feature, BillingPlan.Trial);
        return plan >= minPlan;
    }

    public BillingPlan GetMinimumPlan(PlanFeature feature)
        => FeatureMinPlan.GetValueOrDefault(feature, BillingPlan.Trial);

    // ── Numeric Limits ────────────────────────────────────────────────────────

    public int GetMaxTeamMembers(BillingPlan plan) => plan switch
    {
        BillingPlan.Trial      => 2,
        BillingPlan.Starter    => 5,
        BillingPlan.Pro        => 25,
        BillingPlan.Enterprise => int.MaxValue,
        _ => 2
    };

    public int GetMaxApiKeys(BillingPlan plan) => plan switch
    {
        BillingPlan.Trial      => 1,
        BillingPlan.Starter    => 2,
        BillingPlan.Pro        => 10,
        BillingPlan.Enterprise => int.MaxValue,
        _ => 1
    };

    public int GetMaxWebhookEndpoints(BillingPlan plan) => plan switch
    {
        BillingPlan.Trial      => 0,
        BillingPlan.Starter    => 0,  // Webhooks are Pro+
        BillingPlan.Pro        => 5,
        BillingPlan.Enterprise => int.MaxValue,
        _ => 0
    };

    public int GetTransactionLimit(BillingPlan plan) => plan switch
    {
        BillingPlan.Trial      => 100,
        BillingPlan.Starter    => 1_000,
        BillingPlan.Pro        => 10_000,
        BillingPlan.Enterprise => int.MaxValue,
        _ => 100
    };

    public string GetPlanDisplayName(BillingPlan plan) => plan switch
    {
        BillingPlan.Trial      => "Free Trial",
        BillingPlan.Starter    => "Starter",
        BillingPlan.Pro        => "Pro",
        BillingPlan.Enterprise => "Enterprise",
        _ => "Unknown"
    };
}
