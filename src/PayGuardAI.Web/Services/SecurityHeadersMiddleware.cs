namespace PayGuardAI.Web.Services;

/// <summary>
/// Adds security headers to every HTTP response.
/// Covers OWASP recommended headers for enterprise security + SOC 2 compliance.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Prevent clickjacking — only allow framing from the same origin
        context.Response.Headers["X-Frame-Options"] = "DENY";

        // Prevent MIME-type sniffing attacks
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        // Enable XSS protection in older browsers (modern browsers use CSP)
        context.Response.Headers["X-XSS-Protection"] = "1; mode=block";

        // Referrer policy — send origin only for cross-origin requests
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // Permissions policy — disable unnecessary browser features
        context.Response.Headers["Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=(), payment=(), usb=(), magnetometer=(), gyroscope=(), accelerometer=()";

        // Content-Security-Policy — prevent XSS by restricting resource loading.
        // Blazor Server requires 'unsafe-inline' for styles and 'unsafe-eval' for scripts
        // because the SignalR connection and Blazor runtime need inline code execution.
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
            "font-src 'self' https://fonts.gstatic.com data:; " +
            "img-src 'self' data: https:; " +
            "connect-src 'self' ws: wss:; " +   // SignalR WebSocket connections
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self';";

        // Cache control for API responses (don't cache sensitive data)
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            context.Response.Headers["Pragma"] = "no-cache";
        }

        await _next(context);
    }
}
