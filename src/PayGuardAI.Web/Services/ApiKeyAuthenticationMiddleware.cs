using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PayGuardAI.Data;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Validates X-API-Key header on API endpoints (webhooks, transactions).
/// Looks up the SHA-256 hash of the provided key against stored ApiKey entities.
/// Updates LastUsedAt timestamp on successful authentication.
/// </summary>
public class ApiKeyAuthenticationMiddleware
{
    private const string ApiKeyHeader = "X-API-Key";
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;

    public ApiKeyAuthenticationMiddleware(RequestDelegate next, ILogger<ApiKeyAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only apply to /api/ endpoints (webhooks, transactions, etc.)
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        // Allow health, simulate, and metrics endpoints without API key
        var path = context.Request.Path.Value?.ToLower() ?? "";
        if (path.EndsWith("/health") || path.EndsWith("/simulate") || path.EndsWith("/metrics"))
        {
            await _next(context);
            return;
        }

        // Check for API key header
        var apiKey = context.Request.Headers[ApiKeyHeader].FirstOrDefault();
        if (string.IsNullOrEmpty(apiKey))
        {
            // No API key provided — allow request but mark as unauthenticated.
            // Webhook signature verification is the primary auth for webhooks;
            // API key is an additional layer for enterprise tenants.
            await _next(context);
            return;
        }

        // Validate API key against database
        using var scope = context.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var keyHash = ComputeKeyHash(apiKey);
        var storedKey = await db.ApiKeys
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash && k.IsActive);

        if (storedKey == null)
        {
            _logger.LogWarning("Invalid API key presented: {KeyPrefix}...", apiKey[..Math.Min(8, apiKey.Length)]);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "Invalid or revoked API key" });
            return;
        }

        // Check expiration
        if (storedKey.ExpiresAt.HasValue && storedKey.ExpiresAt.Value < DateTime.UtcNow)
        {
            _logger.LogWarning("Expired API key used: {KeyPrefix}", storedKey.KeyPrefix);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "API key has expired" });
            return;
        }

        // Set tenant context from API key
        context.Items["TenantId"] = storedKey.TenantId;
        context.Items["ApiKeyId"] = storedKey.Id;
        context.Items["ApiKeyScopes"] = storedKey.Scopes;

        _logger.LogDebug("API key authenticated: {KeyPrefix} for tenant {TenantId}",
            storedKey.KeyPrefix, storedKey.TenantId);

        // Update LastUsedAt (fire-and-forget, don't block the request)
        storedKey.LastUsedAt = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Non-critical — don't fail the request if LastUsedAt update fails
            _logger.LogWarning(ex, "Failed to update LastUsedAt for API key {KeyPrefix}", storedKey.KeyPrefix);
        }

        await _next(context);
    }

    private static string ComputeKeyHash(string rawKey)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));
    }
}
