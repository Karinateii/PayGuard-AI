using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PayGuardAI.Web.Controllers;

/// <summary>
/// Authentication controller for demo mode login/logout
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ILogger<AuthController> _logger;

    public AuthController(ILogger<AuthController> logger)
    {
        _logger = logger;
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
