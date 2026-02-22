using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Custom authorization requirement that checks for a specific permission.
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public Permission Permission { get; }

    public PermissionRequirement(Permission permission)
    {
        Permission = permission;
    }
}

/// <summary>
/// Authorization handler that resolves the user's role and checks
/// whether it grants the required permission via the RBAC service.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PermissionAuthorizationHandler> _logger;

    public PermissionAuthorizationHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<PermissionAuthorizationHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        if (roles.Count == 0)
            return;

        using var scope = _scopeFactory.CreateScope();
        var rbacService = scope.ServiceProvider.GetRequiredService<IRbacService>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        // Resolve tenant from the scoped TenantContext (set by TenantResolutionMiddleware)
        var tenantId = tenantContext.TenantId;
        foreach (var role in roles)
        {
            try
            {
                if (await rbacService.HasPermissionAsync(tenantId, role, requirement.Permission))
                {
                    context.Succeed(requirement);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check permission {Permission} for role {Role}", 
                    requirement.Permission, role);
            }
        }
    }
}
