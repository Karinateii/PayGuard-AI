using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// RBAC service — manages custom roles, permissions, team role assignments, and audit trails.
/// </summary>
public class RbacService : IRbacService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<RbacService> _logger;

    public RbacService(ApplicationDbContext db, ILogger<RbacService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Roles ──────────────────────────────────────────────────────────────

    public async Task<List<CustomRole>> GetRolesAsync(string tenantId, CancellationToken ct = default)
    {
        // Ensure system roles exist for this tenant
        await EnsureSystemRolesAsync(tenantId, ct);

        return await _db.CustomRoles
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.IsSystem ? 0 : 1)
            .ThenBy(r => r.Name)
            .ToListAsync(ct);
    }

    public async Task<CustomRole?> GetRoleByIdAsync(Guid roleId, CancellationToken ct = default)
        => await _db.CustomRoles.FindAsync([roleId], ct);

    public async Task<CustomRole?> GetRoleByNameAsync(string tenantId, string roleName, CancellationToken ct = default)
        => await _db.CustomRoles.FirstOrDefaultAsync(
            r => r.TenantId == tenantId && r.Name.ToLower() == roleName.ToLower(), ct);

    public async Task<CustomRole> CreateRoleAsync(string tenantId, string name, string? description,
        IEnumerable<Permission> permissions, string createdBy, CancellationToken ct = default)
    {
        // Validate name uniqueness
        var existing = await GetRoleByNameAsync(tenantId, name, ct);
        if (existing != null)
            throw new InvalidOperationException($"A role named '{name}' already exists.");

        var role = new CustomRole
        {
            TenantId = tenantId,
            Name = name,
            Description = description,
            IsSystem = false,
            CreatedBy = createdBy
        };
        role.SetPermissions(permissions);

        _db.CustomRoles.Add(role);

        // Audit log
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            Action = "ROLE_CREATED",
            EntityType = "CustomRole",
            EntityId = role.Id.ToString(),
            PerformedBy = createdBy,
            NewValues = JsonSerializer.Serialize(new { role.Name, role.Description, role.Permissions }),
            Notes = $"Created custom role '{name}' with {permissions.Count()} permissions"
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Created custom role '{RoleName}' for tenant {TenantId} by {User}",
            name, tenantId, createdBy);
        return role;
    }

    public async Task<CustomRole> UpdateRoleAsync(Guid roleId, string name, string? description,
        IEnumerable<Permission> permissions, string updatedBy, CancellationToken ct = default)
    {
        var role = await _db.CustomRoles.FindAsync([roleId], ct)
            ?? throw new InvalidOperationException("Role not found.");

        if (role.IsSystem)
            throw new InvalidOperationException("System roles cannot be modified.");

        // Check name uniqueness (exclude self)
        var existing = await _db.CustomRoles.FirstOrDefaultAsync(
            r => r.TenantId == role.TenantId && r.Name.ToLower() == name.ToLower() && r.Id != roleId, ct);
        if (existing != null)
            throw new InvalidOperationException($"A role named '{name}' already exists.");

        var oldValues = new { role.Name, role.Description, role.Permissions };

        role.Name = name;
        role.Description = description;
        role.SetPermissions(permissions);
        role.UpdatedAt = DateTime.UtcNow;
        role.UpdatedBy = updatedBy;

        // Audit log
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = role.TenantId,
            Action = "ROLE_UPDATED",
            EntityType = "CustomRole",
            EntityId = role.Id.ToString(),
            PerformedBy = updatedBy,
            OldValues = JsonSerializer.Serialize(oldValues),
            NewValues = JsonSerializer.Serialize(new { role.Name, role.Description, role.Permissions }),
            Notes = $"Updated role '{name}'"
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Updated role '{RoleName}' ({RoleId}) by {User}", name, roleId, updatedBy);
        return role;
    }

    public async Task DeleteRoleAsync(Guid roleId, string deletedBy, CancellationToken ct = default)
    {
        var role = await _db.CustomRoles.FindAsync([roleId], ct)
            ?? throw new InvalidOperationException("Role not found.");

        if (role.IsSystem)
            throw new InvalidOperationException("System roles cannot be deleted.");

        // Check if any team members are assigned to this role
        var assignedCount = await _db.TeamMembers
            .CountAsync(m => m.TenantId == role.TenantId && m.Role == role.Name, ct);
        if (assignedCount > 0)
            throw new InvalidOperationException(
                $"Cannot delete role '{role.Name}' — {assignedCount} team member(s) are still assigned to it. Reassign them first.");

        // Audit log
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = role.TenantId,
            Action = "ROLE_DELETED",
            EntityType = "CustomRole",
            EntityId = role.Id.ToString(),
            PerformedBy = deletedBy,
            OldValues = JsonSerializer.Serialize(new { role.Name, role.Description, role.Permissions }),
            Notes = $"Deleted custom role '{role.Name}'"
        });

        _db.CustomRoles.Remove(role);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Deleted role '{RoleName}' ({RoleId}) by {User}", role.Name, roleId, deletedBy);
    }

    // ── Permissions ────────────────────────────────────────────────────────

    public List<PermissionInfo> GetAllPermissions() =>
    [
        new() { Permission = Permission.ViewTransactions, Name = "View Transactions", Description = "View the transaction list and details", Category = "Transactions" },
        new() { Permission = Permission.ReviewTransactions, Name = "Review Transactions", Description = "Approve, reject, or escalate flagged transactions", Category = "Transactions" },
        new() { Permission = Permission.ManageRules, Name = "Manage Rules", Description = "Create, edit, and toggle risk rules", Category = "Risk" },
        new() { Permission = Permission.ViewReports, Name = "View Reports", Description = "Access compliance reports and analytics dashboards", Category = "Reporting" },
        new() { Permission = Permission.ViewAuditLog, Name = "View Audit Log", Description = "View the full audit trail of system actions", Category = "Reporting" },
        new() { Permission = Permission.ManageTeam, Name = "Manage Team", Description = "Invite, remove, and change roles of team members", Category = "Admin" },
        new() { Permission = Permission.ManageRoles, Name = "Manage Roles", Description = "Create, edit, and delete custom roles", Category = "Admin" },
        new() { Permission = Permission.ManageApiKeys, Name = "Manage API Keys", Description = "Generate and revoke API keys", Category = "Admin" },
        new() { Permission = Permission.ManageWebhooks, Name = "Manage Webhooks", Description = "Configure webhook endpoints", Category = "Admin" },
        new() { Permission = Permission.ManageSettings, Name = "Manage Settings", Description = "Change organization settings (name, thresholds, IP whitelist)", Category = "Admin" },
        new() { Permission = Permission.ManageBilling, Name = "Manage Billing", Description = "View invoices and change subscription plan", Category = "Billing" },
        new() { Permission = Permission.ManageNotifications, Name = "Manage Notifications", Description = "Configure email and Slack notification preferences", Category = "Admin" },
    ];

    public async Task<bool> HasPermissionAsync(string tenantId, string roleName, Permission permission, CancellationToken ct = default)
    {
        var permissions = await GetPermissionsForRoleAsync(tenantId, roleName, ct);
        return permissions.Contains(permission);
    }

    public async Task<List<Permission>> GetPermissionsForRoleAsync(string tenantId, string roleName, CancellationToken ct = default)
    {
        // Check built-in roles first (fast path)
        var builtIn = GetBuiltInPermissions(roleName);
        if (builtIn != null) return builtIn;

        // Look up custom role
        var role = await GetRoleByNameAsync(tenantId, roleName, ct);
        return role?.GetPermissions() ?? [];
    }

    // ── Audit ──────────────────────────────────────────────────────────────

    public async Task<List<AuditLog>> GetRbacAuditLogAsync(string tenantId, int limit = 50, CancellationToken ct = default)
    {
        return await _db.AuditLogs
            .Where(a => a.EntityType == "CustomRole" || a.EntityType == "TeamMember"
                || a.Action.StartsWith("ROLE_") || a.Action.StartsWith("MEMBER_ROLE_"))
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    // ── Team Role Assignment ───────────────────────────────────────────────

    public async Task AssignRoleAsync(Guid memberId, string newRole, string changedBy, CancellationToken ct = default)
    {
        var member = await _db.TeamMembers.FindAsync([memberId], ct)
            ?? throw new InvalidOperationException("Team member not found.");

        var oldRole = member.Role;
        if (oldRole == newRole) return;

        member.Role = newRole;

        // Audit log
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = member.TenantId,
            Action = "MEMBER_ROLE_CHANGED",
            EntityType = "TeamMember",
            EntityId = member.Id.ToString(),
            PerformedBy = changedBy,
            OldValues = JsonSerializer.Serialize(new { Role = oldRole }),
            NewValues = JsonSerializer.Serialize(new { Role = newRole }),
            Notes = $"Changed {member.DisplayName} ({member.Email}) from '{oldRole}' to '{newRole}'"
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Role changed for {Email}: {OldRole} → {NewRole} by {ChangedBy}",
            member.Email, oldRole, newRole, changedBy);
    }

    // ── Internal ───────────────────────────────────────────────────────────

    private static List<Permission>? GetBuiltInPermissions(string roleName) => roleName switch
    {
        "Admin" => [.. CustomRole.AdminPermissions],
        "Manager" => [.. CustomRole.ManagerPermissions],
        "Reviewer" => [.. CustomRole.ReviewerPermissions],
        _ => null // Not a built-in role — will look up custom role in DB
    };

    /// <summary>Lazily seed the three system roles if they don't exist yet for this tenant.</summary>
    private async Task EnsureSystemRolesAsync(string tenantId, CancellationToken ct)
    {
        var hasSystemRoles = await _db.CustomRoles
            .AnyAsync(r => r.TenantId == tenantId && r.IsSystem, ct);

        if (hasSystemRoles) return;

        var systemRoles = new[]
        {
            CreateSystemRole(tenantId, "Reviewer", "Can view and review flagged transactions",
                CustomRole.ReviewerPermissions),
            CreateSystemRole(tenantId, "Manager", "Can manage rules, team, and review transactions",
                CustomRole.ManagerPermissions),
            CreateSystemRole(tenantId, "Admin", "Full access to all features and settings",
                CustomRole.AdminPermissions),
        };

        _db.CustomRoles.AddRange(systemRoles);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded system roles for tenant {TenantId}", tenantId);
    }

    private static CustomRole CreateSystemRole(string tenantId, string name, string description, Permission[] permissions)
    {
        var role = new CustomRole
        {
            TenantId = tenantId,
            Name = name,
            Description = description,
            IsSystem = true,
            CreatedBy = "system"
        };
        role.SetPermissions(permissions);
        return role;
    }
}
