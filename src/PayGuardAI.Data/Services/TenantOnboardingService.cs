using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Provisions new tenants with all required seed data:
/// organization settings, founding admin user, trial subscription, and default risk rules.
/// </summary>
public partial class TenantOnboardingService : ITenantOnboardingService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<TenantOnboardingService> _logger;

    public TenantOnboardingService(ApplicationDbContext db, ILogger<TenantOnboardingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TenantProvisionResult> ProvisionTenantAsync(
        string organizationName,
        string adminEmail,
        string adminDisplayName,
        CancellationToken ct = default)
    {
        // Generate a URL-safe tenant slug from the org name
        var tenantId = GenerateTenantId(organizationName);

        // Check for duplicates
        if (await TenantExistsAsync(tenantId, ct))
        {
            // Append a random suffix to make it unique
            tenantId = $"{tenantId}-{Guid.NewGuid().ToString("N")[..6]}";
        }

        _logger.LogInformation("Provisioning new tenant: {TenantId} for {OrgName}", tenantId, organizationName);

        // 1. Organization settings
        var settings = new OrganizationSettings
        {
            TenantId = tenantId,
            OrganizationName = organizationName,
            SupportEmail = adminEmail,
            Timezone = "UTC",
            DefaultCurrency = "USD",
            AutoApproveThreshold = 20,
            AutoRejectThreshold = 80
        };
        _db.OrganizationSettings.Add(settings);

        // 2. Founding admin user
        var adminUser = new TeamMember
        {
            TenantId = tenantId,
            Email = adminEmail,
            DisplayName = adminDisplayName,
            Role = "Admin",
            Status = "active"
        };
        _db.TeamMembers.Add(adminUser);

        // 3. Trial subscription (30-day trial)
        var subscription = new TenantSubscription
        {
            TenantId = tenantId,
            Plan = BillingPlan.Trial,
            Status = "trialing",
            BillingEmail = adminEmail,
            IncludedTransactions = 1000,
            PeriodStart = DateTime.UtcNow,
            PeriodEnd = DateTime.UtcNow.AddDays(30),
            TrialEndsAt = DateTime.UtcNow.AddDays(30)
        };
        _db.TenantSubscriptions.Add(subscription);

        // 4. Default risk rules (cloned from the global/shared rules)
        var rulesSeeded = await SeedDefaultRulesAsync(tenantId, ct);

        // 5. Default notification preferences
        var notificationPref = new NotificationPreference
        {
            TenantId = tenantId,
            Email = adminEmail,
            DisplayName = adminDisplayName,
            RiskAlertsEnabled = true,
            DailySummaryEnabled = true,
            ReviewAssignmentsEnabled = true,
            TeamInvitesEnabled = true,
            SystemAlertsEnabled = true,
            MinimumRiskScoreForAlert = 50
        };
        _db.Set<NotificationPreference>().Add(notificationPref);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Tenant {TenantId} provisioned: org settings, admin user ({Email}), " +
            "30-day trial, {RuleCount} risk rules seeded",
            tenantId, adminEmail, rulesSeeded);

        return new TenantProvisionResult
        {
            TenantId = tenantId,
            Settings = settings,
            AdminUser = adminUser,
            Subscription = subscription,
            RulesSeeded = rulesSeeded
        };
    }

    public async Task<bool> TenantExistsAsync(string tenantId, CancellationToken ct = default)
    {
        // Use IgnoreQueryFilters to check across all tenants
        return await _db.OrganizationSettings
            .IgnoreQueryFilters()
            .AnyAsync(s => s.TenantId == tenantId, ct);
    }

    public async Task<List<TenantSummary>> GetAllTenantsAsync(CancellationToken ct = default)
    {
        // Must use IgnoreQueryFilters to see ALL tenants (super-admin view)
        var orgs = await _db.OrganizationSettings
            .IgnoreQueryFilters()
            .ToListAsync(ct);

        var summaries = new List<TenantSummary>();

        foreach (var org in orgs)
        {
            var tid = org.TenantId;

            var teamCount = await _db.TeamMembers
                .IgnoreQueryFilters()
                .CountAsync(m => m.TenantId == tid, ct);

            var txnCount = await _db.Transactions
                .IgnoreQueryFilters()
                .CountAsync(t => t.TenantId == tid, ct);

            var riskRuleCount = await _db.RiskRules
                .IgnoreQueryFilters()
                .CountAsync(r => r.TenantId == tid, ct);

            var apiKeyCount = await _db.ApiKeys
                .IgnoreQueryFilters()
                .CountAsync(k => k.TenantId == tid, ct);

            var lastTxn = await _db.Transactions
                .IgnoreQueryFilters()
                .Where(t => t.TenantId == tid)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => (DateTime?)t.CreatedAt)
                .FirstOrDefaultAsync(ct);

            var sub = await _db.TenantSubscriptions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tid, ct);

            var admin = await _db.TeamMembers
                .IgnoreQueryFilters()
                .Where(m => m.TenantId == tid && m.Role == "Admin")
                .Select(m => m.Email)
                .FirstOrDefaultAsync(ct);

            summaries.Add(new TenantSummary
            {
                TenantId = tid,
                OrganizationName = org.OrganizationName,
                AdminEmail = admin ?? org.SupportEmail ?? "",
                Plan = sub?.Plan.ToString() ?? "None",
                Status = sub?.Status ?? "unknown",
                TeamMemberCount = teamCount,
                TransactionCount = txnCount,
                RiskRuleCount = riskRuleCount,
                ApiKeyCount = apiKeyCount,
                TrialEndsAt = sub?.TrialEndsAt,
                CreatedAt = org.CreatedAt,
                LastActivityAt = lastTxn
            });
        }

        return summaries.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public async Task SetTenantStatusAsync(string tenantId, bool isEnabled, CancellationToken ct = default)
    {
        var sub = await _db.TenantSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        if (sub != null)
        {
            sub.Status = isEnabled ? "active" : "disabled";
            sub.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Tenant {TenantId} status changed to {Status}", tenantId, sub.Status);
        }
        else
        {
            _logger.LogWarning("No subscription found for tenant {TenantId} — cannot change status", tenantId);
        }
    }

    public async Task DeleteTenantAsync(string tenantId, CancellationToken ct = default)
    {
        _logger.LogWarning("DELETING tenant {TenantId} and all associated data", tenantId);

        // Delete in dependency order to avoid FK violations
        // 1. Risk factors (depend on risk analyses)
        var riskAnalysisIds = await _db.RiskAnalyses
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId)
            .Select(r => r.Id)
            .ToListAsync(ct);

        if (riskAnalysisIds.Count > 0)
        {
            var riskFactors = await _db.RiskFactors
                .IgnoreQueryFilters()
                .Where(f => riskAnalysisIds.Contains(f.RiskAnalysisId))
                .ToListAsync(ct);
            _db.RiskFactors.RemoveRange(riskFactors);
        }

        // 2. Risk analyses
        var riskAnalyses = await _db.RiskAnalyses
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(ct);
        _db.RiskAnalyses.RemoveRange(riskAnalyses);

        // 3. Transactions
        var transactions = await _db.Transactions
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId)
            .ToListAsync(ct);
        _db.Transactions.RemoveRange(transactions);

        // 4. Risk rules
        var riskRules = await _db.RiskRules
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(ct);
        _db.RiskRules.RemoveRange(riskRules);

        // 5. API keys
        var apiKeys = await _db.ApiKeys
            .IgnoreQueryFilters()
            .Where(k => k.TenantId == tenantId)
            .ToListAsync(ct);
        _db.ApiKeys.RemoveRange(apiKeys);

        // 6. Audit logs
        var auditLogs = await _db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId)
            .ToListAsync(ct);
        _db.AuditLogs.RemoveRange(auditLogs);

        // 7. Customer profiles
        var customers = await _db.CustomerProfiles
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId)
            .ToListAsync(ct);
        _db.CustomerProfiles.RemoveRange(customers);

        // 8. Webhook endpoints
        var webhooks = await _db.WebhookEndpoints
            .IgnoreQueryFilters()
            .Where(w => w.TenantId == tenantId)
            .ToListAsync(ct);
        _db.WebhookEndpoints.RemoveRange(webhooks);

        // 9. Notification preferences
        var notifPrefs = await _db.NotificationPreferences
            .IgnoreQueryFilters()
            .Where(n => n.TenantId == tenantId)
            .ToListAsync(ct);
        _db.NotificationPreferences.RemoveRange(notifPrefs);

        // 10. Magic link tokens
        var magicLinks = await _db.MagicLinkTokens
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId)
            .ToListAsync(ct);
        _db.MagicLinkTokens.RemoveRange(magicLinks);

        // 10b. Custom roles
        var customRoles = await _db.CustomRoles
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(ct);
        _db.CustomRoles.RemoveRange(customRoles);

        // 11. Team members
        var teamMembers = await _db.TeamMembers
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId)
            .ToListAsync(ct);
        _db.TeamMembers.RemoveRange(teamMembers);

        // 12. Subscription
        var subscription = await _db.TenantSubscriptions
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId)
            .ToListAsync(ct);
        _db.TenantSubscriptions.RemoveRange(subscription);

        // 13. Organization settings (last — the "parent" record)
        var settings = await _db.OrganizationSettings
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId)
            .ToListAsync(ct);
        _db.OrganizationSettings.RemoveRange(settings);

        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Tenant {TenantId} fully deleted: {TxnCount} transactions, {TeamCount} team members, " +
            "{RuleCount} rules, {LogCount} audit logs removed",
            tenantId, transactions.Count, teamMembers.Count, riskRules.Count, auditLogs.Count);
    }

    public async Task UpdateOnboardingSettingsAsync(
        string tenantId,
        string timezone,
        string defaultCurrency,
        int autoApproveThreshold,
        int autoRejectThreshold,
        CancellationToken ct = default)
    {
        var settings = await _db.OrganizationSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found");

        settings.Timezone = timezone;
        settings.DefaultCurrency = defaultCurrency;
        settings.AutoApproveThreshold = autoApproveThreshold;
        settings.AutoRejectThreshold = autoRejectThreshold;
        settings.OnboardingCompleted = true;
        settings.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Onboarding settings updated for {TenantId}: tz={TZ}, currency={Currency}, approve≤{Approve}, reject≥{Reject}",
            tenantId, timezone, defaultCurrency, autoApproveThreshold, autoRejectThreshold);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private async Task<int> SeedDefaultRulesAsync(string tenantId, CancellationToken ct)
    {
        // Clone the global shared rules (TenantId == "") as tenant-specific rules
        var sharedRules = await _db.RiskRules
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == "")
            .ToListAsync(ct);

        var count = 0;
        foreach (var rule in sharedRules)
        {
            _db.RiskRules.Add(new RiskRule
            {
                TenantId = tenantId,
                RuleCode = rule.RuleCode,
                Name = rule.Name,
                Description = rule.Description,
                Category = rule.Category,
                Threshold = rule.Threshold,
                ScoreWeight = rule.ScoreWeight,
                IsEnabled = rule.IsEnabled,
                UpdatedBy = "system"
            });
            count++;
        }

        return count;
    }

    private static string GenerateTenantId(string organizationName)
    {
        // Convert to lowercase, replace non-alphanumeric with hyphens, collapse multiples
        var slug = organizationName.Trim().ToLowerInvariant();
        slug = SlugPattern().Replace(slug, "-");
        slug = MultiHyphen().Replace(slug, "-");
        slug = slug.Trim('-');

        // Limit length
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');

        return string.IsNullOrEmpty(slug) ? $"tenant-{Guid.NewGuid().ToString("N")[..8]}" : slug;
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SlugPattern();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex MultiHyphen();
}
