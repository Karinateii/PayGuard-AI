using PayGuardAI.Core.Services;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Resolves tenant context from authenticated user claims, API key header, or config default.
/// Must run AFTER authentication middleware so user claims are available.
/// 
/// Resolution order:
/// 1. Authenticated user's "tenant_id" claim (set by OAuth/Demo auth handlers)
/// 2. X-Tenant-Id header (for API key access — validated by ApiKeyAuthMiddleware)
/// 3. Config default (MultiTenancy:DefaultTenantId)
/// </summary>
public class TenantResolutionMiddleware
{
    private const string TenantHeader = "X-Tenant-Id";
    private const string TenantClaimType = "tenant_id";
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public TenantResolutionMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var defaultTenant = _configuration["MultiTenancy:DefaultTenantId"] ?? "afriex-demo";

        // 1. Check authenticated user's tenant claim (most authoritative)
        var claimTenant = context.User?.FindFirst(TenantClaimType)?.Value;
        if (!string.IsNullOrWhiteSpace(claimTenant))
        {
            tenantContext.TenantId = claimTenant;
        }
        // 2. Check header (for API-key authenticated requests)
        else if (context.Request.Headers.TryGetValue(TenantHeader, out var headerTenant)
                 && !string.IsNullOrWhiteSpace(headerTenant.FirstOrDefault()))
        {
            tenantContext.TenantId = headerTenant.FirstOrDefault()!;
        }
        // 3. Fall back to config default
        else
        {
            tenantContext.TenantId = defaultTenant;
        }

        context.Items["TenantId"] = tenantContext.TenantId;
        await _next(context);
    }
}
