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
        // Check if user has authenticated via session cookie
        var isAuthenticated = Context.Session.GetString("IsAuthenticated");
        
        // If not authenticated and not trying to login, fail
        if (isAuthenticated != "true")
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userHeader = Request.Headers["X-Demo-User"].FirstOrDefault();
        var rolesHeader = Request.Headers["X-Demo-Roles"].FirstOrDefault();

        var userName = string.IsNullOrWhiteSpace(userHeader)
            ? _configuration["Auth:DefaultUser"] ?? "demo@payguard.ai"
            : userHeader;

        var roles = string.IsNullOrWhiteSpace(rolesHeader)
            ? _configuration["Auth:DefaultRoles"] ?? "Reviewer,Manager"
            : rolesHeader;

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, userName),
            new(ClaimTypes.NameIdentifier, userName)
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
}
