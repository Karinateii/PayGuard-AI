using System.Diagnostics;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Logs request duration for basic observability.
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
        var sw = Stopwatch.StartNew();
        await _next(context);
        sw.Stop();

        if (sw.ElapsedMilliseconds > 1500)
        {
            _logger.LogWarning("Slow request: {Method} {Path} took {Elapsed}ms", context.Request.Method, context.Request.Path, sw.ElapsedMilliseconds);
        }
        else
        {
            _logger.LogInformation("Request: {Method} {Path} took {Elapsed}ms", context.Request.Method, context.Request.Path, sw.ElapsedMilliseconds);
        }
    }
}
