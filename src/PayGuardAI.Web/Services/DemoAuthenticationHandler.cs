using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Demo authentication handler that creates a user from headers or defaults.
/// Checks session to determine if user is actually logged in.
/// </summary>
public class DemoAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IConfiguration _configuration;

    public DemoAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var path = Context.Request.Path;
        Logger.LogInformation("[AUTH] Checking authentication for path: {Path}", path);
        
        // Check if user has authenticated via session cookie
        var isAuthenticated = Context.Session.GetString("IsAuthenticated");
        Logger.LogInformation("[AUTH] Session IsAuthenticated value: {Value}", isAuthenticated ?? "(null)");
        
        // If not authenticated and not trying to login, fail
        if (isAuthenticated != "true")
        {
            Logger.LogInformation("[AUTH] Not authenticated - returning NoResult");
            return Task.FromResult(AuthenticateResult.NoResult());
        }
        
        Logger.LogInformation("[AUTH] User is authenticated - creating claims principal");

        // User and roles come ONLY from config — never from request headers
        // (headers could be spoofed for privilege escalation)
        var userName = _configuration["Auth:DefaultUser"] ?? "compliance_officer@payguard.ai";
        var roles = _configuration["Auth:DefaultRoles"] ?? "Reviewer,Manager";

        var tenantId = _configuration["MultiTenancy:DefaultTenantId"] ?? "afriex-demo";

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, userName),
            new(ClaimTypes.NameIdentifier, userName),
            new("tenant_id", tenantId)
        };

        foreach (var role in roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Logger.LogInformation("[AUTH] HandleChallengeAsync called - redirecting to /login");
        Response.Redirect("/login");
        return Task.CompletedTask;
    }
}
