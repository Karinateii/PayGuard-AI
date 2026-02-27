namespace PayGuardAI.Web.Services;

/// <summary>
/// Input validation middleware for all API endpoints.
/// Enforces maximum payload sizes, content type requirements, and
/// rejects obviously malicious payloads before they reach controllers.
/// </summary>
public class InputValidationMiddleware
{
    private const long MaxPayloadSize = 1_048_576; // 1 MB max for webhook payloads
    private const long MaxSimulatePayloadSize = 10_240; // 10 KB max for simulate
    private readonly RequestDelegate _next;
    private readonly ILogger<InputValidationMiddleware> _logger;

    public InputValidationMiddleware(RequestDelegate next, ILogger<InputValidationMiddleware> logger)
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

        // Skip GET requests (health, metrics)
        if (HttpMethods.IsGet(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Skip auth endpoints (demo-login uses form POST, not JSON)
        // Supports both /api/Auth and /api/v{n}/Auth versioned routes
        if (context.Request.Path.StartsWithSegments("/api/Auth")
            || (context.Request.Path.HasValue
                && context.Request.Path.Value!.StartsWith("/api/v", StringComparison.OrdinalIgnoreCase)
                && context.Request.Path.Value.Contains("/Auth", StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // Enforce Content-Type on POST/PUT/PATCH for webhook/data endpoints
        if (HttpMethods.IsPost(context.Request.Method) ||
            HttpMethods.IsPut(context.Request.Method) ||
            HttpMethods.IsPatch(context.Request.Method))
        {
            var contentType = context.Request.ContentType;
            if (!string.IsNullOrEmpty(contentType) &&
                !contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase) &&
                !contentType.Contains("text/json", StringComparison.OrdinalIgnoreCase) &&
                !contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Rejected request with invalid content type: {ContentType} to {Path}",
                    contentType, context.Request.Path);
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "Content-Type must be application/json"
                });
                return;
            }
        }

        // Enforce payload size limits
        if (context.Request.ContentLength.HasValue)
        {
            var maxSize = context.Request.Path.Value?.Contains("/simulate") == true
                ? MaxSimulatePayloadSize
                : MaxPayloadSize;

            if (context.Request.ContentLength.Value > maxSize)
            {
                _logger.LogWarning("Rejected oversized payload: {Size} bytes to {Path} (max: {MaxSize})",
                    context.Request.ContentLength.Value, context.Request.Path, maxSize);
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    error = $"Payload exceeds maximum size of {maxSize / 1024} KB"
                });
                return;
            }
        }

        // Block requests with suspicious headers
        var userAgent = context.Request.Headers.UserAgent.FirstOrDefault() ?? "";
        if (ContainsSqlInjection(userAgent) || ContainsSqlInjection(context.Request.QueryString.Value ?? ""))
        {
            _logger.LogWarning("Blocked suspicious request to {Path}: potential injection in headers/query",
                context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "Invalid request" });
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Basic SQL injection detection for headers and query strings.
    /// Not a replacement for parameterized queries — this is defense-in-depth.
    /// </summary>
    private static bool ContainsSqlInjection(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;

        var lowerInput = input.ToLowerInvariant();
        string[] patterns =
        [
            "'; drop ",
            "'; delete ",
            "' or '1'='1",
            "' or 1=1",
            "union select",
            "exec xp_",
            "exec sp_",
            "'; exec ",
            "<script>",
            "javascript:",
            "onerror="
        ];

        return patterns.Any(p => lowerInput.Contains(p));
    }
}
