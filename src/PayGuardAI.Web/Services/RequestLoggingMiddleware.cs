using Serilog.Context;
using System.Diagnostics;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Enriches every request with a correlation ID and tenant context in Serilog's LogContext.
/// Also warns on slow requests (>1500ms) for performance tracking.
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Generate or forward a correlation ID for request tracing across logs
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N")[..12];

        context.Response.Headers["X-Correlation-ID"] = correlationId;

        var tenantId = context.Items["TenantId"] as string ?? "unknown";
        var userId = context.User?.Identity?.Name ?? "anonymous";

        // Push structured properties into Serilog LogContext for ALL logs in this request
        using var correlationProperty = LogContext.PushProperty("CorrelationId", correlationId);
        using var tenantProperty = LogContext.PushProperty("TenantId", tenantId);
        using var userProperty = LogContext.PushProperty("UserId", userId);

        var sw = Stopwatch.StartNew();
        await _next(context);
        sw.Stop();

        // Warn on slow requests — useful for Railway performance monitoring
        if (sw.ElapsedMilliseconds > 1500)
        {
            _logger.LogWarning(
                "Slow request detected: {Method} {Path} took {ElapsedMs}ms (Status: {StatusCode}, CorrelationId: {CorrelationId})",
                context.Request.Method,
                context.Request.Path,
                sw.ElapsedMilliseconds,
                context.Response.StatusCode,
                correlationId);
        }
    }
}
