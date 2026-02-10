using PayGuardAI.Core.Services;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Resolves tenant context from headers or configuration.
/// </summary>
public class TenantResolutionMiddleware
{
    private const string TenantHeader = "X-Tenant-Id";
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public TenantResolutionMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var headerTenant = context.Request.Headers[TenantHeader].FirstOrDefault();
        var defaultTenant = _configuration["MultiTenancy:DefaultTenantId"] ?? "afriex-demo";

        tenantContext.TenantId = string.IsNullOrWhiteSpace(headerTenant) ? defaultTenant : headerTenant;
        context.Items["TenantId"] = tenantContext.TenantId;

        await _next(context);
    }
}
