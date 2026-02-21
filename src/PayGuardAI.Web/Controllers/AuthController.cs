using Microsoft.AspNetCore.Authentication;
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
    /// Demo logout endpoint - clears session and redirects to login
    /// </summary>
    [HttpGet("demo-logout")]
    [AllowAnonymous]
    public IActionResult DemoLogout()
    {
        _logger.LogInformation("[AUTH-CONTROLLER] Demo logout requested");
        
        // Clear session
        HttpContext.Session.Clear();
        
        _logger.LogInformation("[AUTH-CONTROLLER] Session cleared, redirecting to /login");
        
        // Redirect to login page
        return Redirect("/login");
    }
}
