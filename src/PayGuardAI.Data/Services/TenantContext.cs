using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Default tenant context implementation.
/// </summary>
public class TenantContext : ITenantContext
{
    public string TenantId { get; set; } = "afriex-demo";
}
