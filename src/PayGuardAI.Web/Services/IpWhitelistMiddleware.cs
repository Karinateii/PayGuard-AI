using System.Net;
using Microsoft.EntityFrameworkCore;
using PayGuardAI.Data;

namespace PayGuardAI.Web.Services;

/// <summary>
/// IP whitelisting middleware for API endpoints.
/// Checks the tenant's OrganizationSettings.IpWhitelist to restrict access
/// to specified IP addresses. If no whitelist is configured, all IPs are allowed.
/// </summary>
public class IpWhitelistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IpWhitelistMiddleware> _logger;

    public IpWhitelistMiddleware(RequestDelegate next, ILogger<IpWhitelistMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only apply to /api/ endpoints
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        // Allow health and metrics endpoints without IP check
        var path = context.Request.Path.Value?.ToLower() ?? "";
        if (path.EndsWith("/health") || path.EndsWith("/metrics"))
        {
            await _next(context);
            return;
        }

        var tenantId = context.Items["TenantId"] as string;
        if (string.IsNullOrEmpty(tenantId))
        {
            await _next(context);
            return;
        }

        using var scope = context.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Core.Entities.OrganizationSettings? settings;
        try
        {
            settings = await db.OrganizationSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query IP whitelist for tenant {TenantId}. Allowing request.", tenantId);
            await _next(context);
            return;
        }

        // If no whitelist configured, allow all IPs
        if (settings == null || string.IsNullOrWhiteSpace(settings.IpWhitelist))
        {
            await _next(context);
            return;
        }

        // Get client IP
        var clientIp = GetClientIpAddress(context);
        if (clientIp == null)
        {
            _logger.LogWarning("Could not determine client IP for tenant {TenantId}", tenantId);
            await _next(context); // Allow if IP can't be determined
            return;
        }

        // Parse whitelist (comma-separated or newline-separated)
        var allowedIps = settings.IpWhitelist
            .Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(ip => ip.Trim())
            .Where(ip => !string.IsNullOrEmpty(ip))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var clientIpStr = clientIp.ToString();

        if (!allowedIps.Contains(clientIpStr))
        {
            _logger.LogWarning(
                "IP {ClientIp} not in whitelist for tenant {TenantId}. Allowed: {WhitelistCount} IPs",
                clientIpStr, tenantId, allowedIps.Count);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = "IP address not allowed. Contact your administrator to update the IP whitelist."
            });
            return;
        }

        _logger.LogDebug("IP {ClientIp} allowed for tenant {TenantId}", clientIpStr, tenantId);
        await _next(context);
    }

    private static IPAddress? GetClientIpAddress(HttpContext context)
    {
        // Check X-Forwarded-For first (Railway, Heroku, load balancers)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            var firstIp = forwardedFor.Split(',')[0].Trim();
            if (IPAddress.TryParse(firstIp, out var ip))
                return ip;
        }

        // Check X-Real-IP (nginx)
        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp) && IPAddress.TryParse(realIp, out var realIpAddr))
            return realIpAddr;

        // Fall back to connection remote IP
        return context.Connection.RemoteIpAddress;
    }
}
