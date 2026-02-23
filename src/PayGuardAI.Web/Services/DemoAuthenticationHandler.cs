using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayGuardAI.Data;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Demo authentication handler that creates a user from headers or defaults.
/// Checks session to determine if user is actually logged in.
/// </summary>
public class DemoAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _db;

    public DemoAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration,
        ApplicationDbContext db)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
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
            return AuthenticateResult.NoResult();
        }
        
        Logger.LogInformation("[AUTH] User is authenticated - creating claims principal");

        // If the user signed in via magic link, use the email from the session
        // Otherwise fall back to the config default (platform owner)
        var sessionEmail = Context.Session.GetString("AuthenticatedEmail");
        var userName = !string.IsNullOrEmpty(sessionEmail)
            ? sessionEmail
            : _configuration["Auth:DefaultUser"] ?? "compliance_officer@payguard.ai";

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, userName),
            new(ClaimTypes.NameIdentifier, userName)
        };

        var platformOwnerEmail = _configuration["Auth:DefaultUser"];
        var defaultTenantId = _configuration["MultiTenancy:DefaultTenantId"] ?? "afriex-demo";

        // ── Platform owner shortcut ──────────────────────────────────
        // If this email matches Auth:DefaultUser, always SuperAdmin.
        if (!string.IsNullOrEmpty(platformOwnerEmail) &&
            string.Equals(userName, platformOwnerEmail, StringComparison.OrdinalIgnoreCase))
        {
            claims.Add(new Claim("tenant_id", defaultTenantId));
            claims.Add(new Claim(ClaimTypes.Role, "SuperAdmin"));
            Logger.LogInformation("[AUTH] Platform owner {User} → SuperAdmin in {Tenant}",
                userName, defaultTenantId);

            // Auto-create SuperAdmin TeamMember if missing
            var hasSuperAdmin = await _db.TeamMembers
                .IgnoreQueryFilters()
                .AnyAsync(t => t.Email == userName
                            && t.TenantId == defaultTenantId
                            && t.Role == "SuperAdmin");
            if (!hasSuperAdmin)
            {
                _db.TeamMembers.Add(new PayGuardAI.Core.Entities.TeamMember
                {
                    TenantId = defaultTenantId,
                    Email = userName,
                    DisplayName = "Platform Owner",
                    Role = "SuperAdmin",
                    Status = "active"
                });
                await _db.SaveChangesAsync();
                Logger.LogInformation("[AUTH] Auto-created SuperAdmin TeamMember for {User}", userName);
            }
        }
        else
        {
            // ── Regular user: look up TeamMember by email ────────────
            var teamMember = await _db.TeamMembers
                .IgnoreQueryFilters()
                .Where(t => t.Email == userName && t.Status == "active")
                .OrderByDescending(t => t.Role == "SuperAdmin" ? 1 : 0)
                .ThenBy(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            if (teamMember != null)
            {
                claims.Add(new Claim("tenant_id", teamMember.TenantId));
                claims.Add(new Claim(ClaimTypes.Role, teamMember.Role));
                Logger.LogInformation("[AUTH] Mapped {User} to tenant {Tenant} role {Role}",
                    userName, teamMember.TenantId, teamMember.Role);
            }
            else
            {
                // Fallback: user not in any org — use config defaults
                var roles = _configuration["Auth:DefaultRoles"] ?? "Reviewer,Manager,Admin,SuperAdmin";
                claims.Add(new Claim("tenant_id", defaultTenantId));
                foreach (var role in roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }
                Logger.LogInformation("[AUTH] No TeamMember for {User}, using default tenant {Tenant}",
                    userName, defaultTenantId);
            }
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Logger.LogInformation("[AUTH] HandleChallengeAsync called - redirecting to /login");
        Response.Redirect("/login");
        return Task.CompletedTask;
    }
}
