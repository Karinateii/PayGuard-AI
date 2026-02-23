using PayGuardAI.Core.Entities;

namespace PayGuardAI.Core.Services;

/// <summary>
/// Service for provisioning new tenants — creates organization settings,
/// founding admin user, trial subscription, and default risk rules.
/// </summary>
public interface ITenantOnboardingService
{
    /// <summary>
    /// Provision a brand-new tenant with all required seed data.
    /// Returns the generated tenant ID.
    /// </summary>
    Task<TenantProvisionResult> ProvisionTenantAsync(
        string organizationName,
        string adminEmail,
        string adminDisplayName,
        CancellationToken ct = default);

    /// <summary>
    /// Check if a tenant ID already exists.
    /// </summary>
    Task<bool> TenantExistsAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Get all tenants for super-admin management.
    /// </summary>
    Task<List<TenantSummary>> GetAllTenantsAsync(CancellationToken ct = default);

    /// <summary>
    /// Enable or disable a tenant.
    /// </summary>
    Task SetTenantStatusAsync(string tenantId, bool isEnabled, CancellationToken ct = default);

    /// <summary>
    /// Permanently delete a tenant and ALL of their data.
    /// </summary>
    Task DeleteTenantAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Update organization settings during onboarding (cross-tenant, no auth context required).
    /// </summary>
    Task UpdateOnboardingSettingsAsync(
        string tenantId,
        string timezone,
        string defaultCurrency,
        int autoApproveThreshold,
        int autoRejectThreshold,
        CancellationToken ct = default);
}

/// <summary>
/// Result of tenant provisioning.
/// </summary>
public class TenantProvisionResult
{
    public string TenantId { get; set; } = string.Empty;
    public OrganizationSettings Settings { get; set; } = null!;
    public TeamMember AdminUser { get; set; } = null!;
    public TenantSubscription Subscription { get; set; } = null!;
    public int RulesSeeded { get; set; }
}

/// <summary>
/// Summary of a tenant for the super-admin dashboard.
/// </summary>
public class TenantSummary
{
    public string TenantId { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public string AdminEmail { get; set; } = string.Empty;
    public string Plan { get; set; } = "Trial";
    public string Status { get; set; } = "active";
    public int TeamMemberCount { get; set; }
    public int TransactionCount { get; set; }
    public int RiskRuleCount { get; set; }
    public int ApiKeyCount { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
}
