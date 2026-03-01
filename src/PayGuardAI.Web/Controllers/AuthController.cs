using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Asp.Versioning;
using PayGuardAI.Core.Services;
using PayGuardAI.Data;

namespace PayGuardAI.Web.Controllers;

/// <summary>
/// Authentication controller for demo mode, OAuth, and magic link login/logout
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Route("api/[controller]")] // Backward-compatible unversioned route
public class AuthController : ControllerBase
{
    private readonly ILogger<AuthController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IMagicLinkService _magicLink;
    private readonly ApplicationDbContext _db;

    public AuthController(
        ILogger<AuthController> logger,
        IConfiguration configuration,
        IMagicLinkService magicLink,
        ApplicationDbContext db)
    {
        _logger = logger;
        _configuration = configuration;
        _magicLink = magicLink;
        _db = db;
    }

    /// <summary>
    /// Demo login endpoint - sets session and redirects to home.
    /// SECURITY: Only available when OAuth is disabled (development/demo mode).
    /// In production (OAuthEnabled=true), this endpoint returns 404.
    /// </summary>
    /// <response code="302">Login successful, redirecting to dashboard</response>
    /// <response code="404">Demo login disabled (OAuth mode is active)</response>
    [HttpPost("demo-login")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DemoLogin()
    {
        // SECURITY: Block demo login when OAuth is enabled (production mode)
        if (_configuration.IsOAuthEnabled())
        {
            _logger.LogWarning("[AUTH] Demo login attempt blocked — OAuth is enabled (production mode)");
            return NotFound();
        }

        _logger.LogInformation("[AUTH-CONTROLLER] Demo login requested");
        
        // Set session to mark user as authenticated
        HttpContext.Session.SetString("IsAuthenticated", "true");
        
        _logger.LogInformation("[AUTH-CONTROLLER] Session set, redirecting to /");
        
        // Redirect to home page
        return Redirect("/");
    }

    /// <summary>
    /// OAuth login endpoint - triggers OIDC challenge (redirects to Google/Azure AD)
    /// </summary>
    /// <response code="302">Redirecting to OAuth provider</response>
    [HttpGet("oauth-login")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult OAuthLogin()
    {
        _logger.LogInformation("[AUTH-CONTROLLER] OAuth login requested");

        var properties = new AuthenticationProperties
        {
            RedirectUri = "/",
            IsPersistent = true
        };

        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Request a magic link — sends a one-time login link to the user's email.
    /// </summary>
    /// <param name="email">Email address to send the magic link to</param>
    /// <response code="302">Redirecting to confirmation page (always, for security)</response>
    [HttpPost("magic-link/request")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> RequestMagicLink([FromForm] string email)
    {
        _logger.LogInformation("[AUTH] Magic link requested for {Email}", email);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _magicLink.SendMagicLinkAsync(email, ip);

        // Always redirect to a confirmation page — don't reveal if email exists
        return Redirect($"/login?magicLinkSent=true&email={Uri.EscapeDataString(email)}");
    }

    /// <summary>
    /// Verify a magic link token — validates it, looks up the user's org, and signs them in.
    /// </summary>
    /// <param name="token">One-time magic link token from the email</param>
    /// <response code="302">Login successful, redirecting to dashboard (or error page)</response>
    [HttpGet("magic-link/verify")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> VerifyMagicLink([FromQuery] string token)
    {
        var email = await _magicLink.ValidateTokenAsync(token);

        if (email is null)
        {
            _logger.LogWarning("[AUTH] Invalid or expired magic link");
            return Redirect("/login?error=invalid_link");
        }

        // Look up the TeamMember to get tenant and role (case-insensitive for PostgreSQL)
        var teamMember = await _db.TeamMembers
            .IgnoreQueryFilters()
            .Where(t => t.Email.ToLower() == email.ToLower() && t.Status == "active")
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        if (teamMember is null)
        {
            _logger.LogWarning("[AUTH] Magic link valid but no active TeamMember for {Email}", email);
            return Redirect("/login?error=no_account");
        }

        // Determine auth mode and sign in accordingly
        var isOAuth = _configuration.IsOAuthEnabled();

        if (isOAuth)
        {
            // OAuth mode: issue a cookie-based sign-in
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, email),
                new(ClaimTypes.NameIdentifier, email),
                new(ClaimTypes.Email, email),
                new(ClaimTypes.Role, teamMember.Role),
                new("tenant_id", teamMember.TenantId)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = true });
        }
        else
        {
            // Demo mode: set session + stash email so DemoAuthHandler can look it up
            HttpContext.Session.SetString("IsAuthenticated", "true");
            HttpContext.Session.SetString("AuthenticatedEmail", email);
        }

        // Record last login time
        teamMember.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("[AUTH] Magic link login successful for {Email} → tenant {Tenant}",
            email, teamMember.TenantId);

        // If MFA is enabled, redirect to verification page
        if (teamMember.MfaEnabled)
        {
            _logger.LogInformation("[AUTH] MFA required for {Email}, redirecting to /mfa/verify", email);
            return Redirect("/mfa/verify");
        }

        return Redirect("/");
    }

    /// <summary>
    /// Unified logout endpoint — handles both Demo and OAuth sign-out.
    /// Clears session and cookies, redirects to login page.
    /// </summary>
    /// <response code="302">Logged out, redirecting to login page</response>
    [HttpGet("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> Logout()
    {
        _logger.LogInformation("[AUTH-CONTROLLER] Logout requested");

        // Always clear session data
        HttpContext.Session.Clear();

        // Always sign out from the cookie auth scheme — clears the auth cookie
        // so the Blazor circuit immediately loses the authenticated state.
        // Without this, the old circuit can briefly show the navbar after logout.
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        // Note: We do NOT sign out from the OIDC provider (Google) itself — that
        // would log the user out of their Google account entirely. We only clear
        // our application cookies so they must re-authenticate with PayGuard AI.

        _logger.LogInformation("[AUTH-CONTROLLER] Session + cookies cleared, redirecting to /login");
        return Redirect("/login");
    }

    /// <summary>
    /// MFA verification complete — marks the session as MFA-verified and redirects to dashboard.
    /// Called after successful TOTP or backup code verification on /mfa/verify.
    /// </summary>
    /// <response code="302">MFA verified, redirecting to dashboard</response>
    [HttpGet("mfa-complete")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult MfaComplete()
    {
        HttpContext.Session.SetString("MfaVerified", "true");
        _logger.LogInformation("[AUTH] MFA verification completed for {User}", User.Identity?.Name);
        return Redirect("/");
    }

    /// <summary>
    /// SuperAdmin: switch to another tenant's context.
    /// Stores the impersonated tenant ID in the session so it survives page loads.
    /// </summary>
    /// <param name="tenantId">Target tenant ID to impersonate</param>
    /// <response code="302">Impersonation active, redirecting to dashboard</response>
    /// <response code="400">Invalid tenant ID</response>
    /// <response code="403">Not a SuperAdmin</response>
    [HttpGet("impersonate/{tenantId}")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult Impersonate(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return BadRequest("tenantId is required");

        HttpContext.Session.SetString("ImpersonateTenantId", tenantId);
        _logger.LogWarning("[AUTH] SuperAdmin {User} impersonating tenant {TenantId}",
            User.Identity?.Name, tenantId);

        return Redirect("/");
    }

    /// <summary>
    /// SuperAdmin: stop impersonating — return to home tenant.
    /// </summary>
    /// <response code="302">Impersonation stopped, redirecting to dashboard</response>
    /// <response code="403">Not a SuperAdmin</response>
    [HttpGet("stop-impersonating")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult StopImpersonating()
    {
        HttpContext.Session.Remove("ImpersonateTenantId");
        _logger.LogInformation("[AUTH] SuperAdmin {User} stopped impersonating",
            User.Identity?.Name);

        return Redirect("/");
    }
}
