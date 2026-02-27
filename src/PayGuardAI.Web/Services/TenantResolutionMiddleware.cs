using Microsoft.EntityFrameworkCore;
using PayGuardAI.Core.Services;
using PayGuardAI.Data;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Resolves tenant context from authenticated user claims, API key header, or config default.
/// Must run AFTER authentication middleware so user claims are available.
/// Sets IsDisabled flag so downstream services can skip transaction processing.
/// 
/// Resolution order:
/// 0. Session impersonation (SuperAdmin)
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

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        var defaultTenant = _configuration["MultiTenancy:DefaultTenantId"] ?? "afriex-demo";

        // 0. Check session for SuperAdmin tenant impersonation (highest priority)
        var impersonatedTenant = context.Session.GetString("ImpersonateTenantId");
        if (!string.IsNullOrWhiteSpace(impersonatedTenant))
        {
            tenantContext.TenantId = impersonatedTenant;
        }
        // 1. Check authenticated user's tenant claim (most authoritative)
        else if (!string.IsNullOrWhiteSpace(context.User?.FindFirst(TenantClaimType)?.Value))
        {
            tenantContext.TenantId = context.User!.FindFirst(TenantClaimType)!.Value;
        }
        // 2. Check header (for API-key authenticated requests)
        else if (context.Request.Headers.TryGetValue(TenantHeader, out var headerTenant)
                 && !string.IsNullOrWhiteSpace(headerTenant.FirstOrDefault()))
        {
            tenantContext.TenantId = headerTenant.FirstOrDefault()!;
        }
        // 3. Fall back to config default (only for non-API requests like Blazor pages)
        //    API endpoints MUST supply tenant via auth claim or X-Tenant-Id header
        //    Exception: webhook endpoints and auth endpoints don't require tenant context
        else if (context.Request.Path.StartsWithSegments("/api")
                 && !IsWebhookPath(context.Request.Path)
                 && !IsAuthPath(context.Request.Path))
        {
            // SECURITY: reject API calls with no identifiable tenant
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = "Missing tenant context. Provide X-Tenant-Id header or authenticate." });
            return;
        }
        else
        {
            tenantContext.TenantId = defaultTenant;
        }

        context.Items["TenantId"] = tenantContext.TenantId;

        // ── Check if tenant is disabled (set flag, don't block login) ─────
        if (!string.IsNullOrWhiteSpace(tenantContext.TenantId))
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var status = await db.TenantSubscriptions
                .IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantContext.TenantId)
                .Select(s => s.Status)
                .FirstOrDefaultAsync();

            tenantContext.IsDisabled = status == "disabled";
        }

        await _next(context);
    }

    /// <summary>
    /// Matches webhook paths: /api/webhooks/... and /api/v{n}/webhooks/...
    /// </summary>
    private static bool IsWebhookPath(PathString path) =>
        path.StartsWithSegments("/api/webhooks")
        || (path.HasValue && path.Value!.StartsWith("/api/v", StringComparison.OrdinalIgnoreCase)
            && path.Value.Contains("/webhooks", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Matches auth paths: /api/Auth/... and /api/v{n}/Auth/...
    /// </summary>
    private static bool IsAuthPath(PathString path) =>
        path.StartsWithSegments("/api/Auth")
        || (path.HasValue && path.Value!.StartsWith("/api/v", StringComparison.OrdinalIgnoreCase)
            && path.Value.Contains("/Auth", StringComparison.OrdinalIgnoreCase));
}
