using System.Security.Claims;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Resolves current user identity from the request context.
/// </summary>
public class CurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string UserName
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.Identity?.Name ?? "demo@payguard.ai";
        }
    }

    public bool IsInRole(string role)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        return user?.IsInRole(role) ?? false;
    }
}
