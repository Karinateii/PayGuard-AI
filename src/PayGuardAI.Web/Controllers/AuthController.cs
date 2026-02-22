using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PayGuardAI.Web.Controllers;

/// <summary>
/// Authentication controller for demo mode login/logout and OAuth
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ILogger<AuthController> _logger;
    private readonly IConfiguration _configuration;

    public AuthController(ILogger<AuthController> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Demo login endpoint - sets session and redirects to home
    /// </summary>
    [HttpPost("demo-login")]
    [AllowAnonymous]
    public IActionResult DemoLogin()
    {
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
    [HttpGet("oauth-login")]
    [AllowAnonymous]
    public IActionResult OAuthLogin()
    {
        _logger.LogInformation("[AUTH-CONTROLLER] OAuth login requested - triggering OIDC challenge");
        
        var properties = new AuthenticationProperties
        {
            RedirectUri = "/",
            IsPersistent = true
        };

        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Unified logout endpoint — handles both Demo and OAuth sign-out
    /// </summary>
    [HttpGet("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        _logger.LogInformation("[AUTH-CONTROLLER] Logout requested");

        // Always clear session data
        HttpContext.Session.Clear();

        // If OAuth is enabled, sign out from both the cookie and OIDC schemes
        var isOAuth = _configuration.IsOAuthEnabled();
        if (isOAuth)
        {
            _logger.LogInformation("[AUTH-CONTROLLER] Clearing OAuth cookies");
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            // Note: We do NOT sign out from the OIDC provider (Google) itself — that
            // would log the user out of their Google account entirely. We only clear
            // our application cookies so they must re-authenticate with PayGuard AI.
        }

        _logger.LogInformation("[AUTH-CONTROLLER] Session + cookies cleared, redirecting to /login");
        return Redirect("/login");
    }
}
