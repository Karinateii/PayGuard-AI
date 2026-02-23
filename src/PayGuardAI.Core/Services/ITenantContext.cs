namespace PayGuardAI.Core.Services;

/// <summary>
/// Provides tenant context for multi-tenant operations.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Gets or sets the current tenant identifier.
    /// </summary>
    string TenantId { get; set; }

    /// <summary>
    /// Whether this tenant's subscription is disabled.
    /// When true, the tenant can still log in but transaction processing is suspended.
    /// </summary>
    bool IsDisabled { get; set; }
}
