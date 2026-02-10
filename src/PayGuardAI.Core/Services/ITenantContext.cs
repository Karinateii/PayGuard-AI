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
}
