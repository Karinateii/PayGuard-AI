using System.Security.Claims;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Resolves current user identity from the request context.
/// Provides role, permission, and tenant information.
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
            return user?.Identity?.Name ?? "unknown";
        }
    }

    /// <summary>Get the tenant ID from the current user's claims.</summary>
    public string TenantId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.FindFirst("tenant_id")?.Value ?? "afriex-demo";
        }
    }

    public bool IsInRole(string role)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        return user?.IsInRole(role) ?? false;
    }

    /// <summary>Get all roles assigned to the current user.</summary>
    public IEnumerable<string> GetRoles()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        return user?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? [];
    }

    /// <summary>Get the user's primary (highest-privilege) role.</summary>
    public string PrimaryRole
    {
        get
        {
            var roles = GetRoles().ToList();
            if (roles.Contains("Admin")) return "Admin";
            if (roles.Contains("Manager")) return "Manager";
            return roles.FirstOrDefault() ?? "Reviewer";
        }
    }
}
