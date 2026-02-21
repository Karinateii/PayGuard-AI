using PayGuardAI.Core.Entities;

namespace PayGuardAI.Core.Services;

/// <summary>
/// Role-based access control service — custom roles, permissions, and audit trails.
/// </summary>
public interface IRbacService
{
    // ── Roles ──────────────────────────────────────────────────────────────

    /// <summary>Get all roles (system + custom) for a tenant.</summary>
    Task<List<CustomRole>> GetRolesAsync(string tenantId, CancellationToken ct = default);

    /// <summary>Get a single role by ID.</summary>
    Task<CustomRole?> GetRoleByIdAsync(Guid roleId, CancellationToken ct = default);

    /// <summary>Get a role by name for a tenant (case-insensitive).</summary>
    Task<CustomRole?> GetRoleByNameAsync(string tenantId, string roleName, CancellationToken ct = default);

    /// <summary>Create a new custom role with specific permissions.</summary>
    Task<CustomRole> CreateRoleAsync(string tenantId, string name, string? description,
        IEnumerable<Permission> permissions, string createdBy, CancellationToken ct = default);

    /// <summary>Update an existing custom role's name, description, or permissions.</summary>
    Task<CustomRole> UpdateRoleAsync(Guid roleId, string name, string? description,
        IEnumerable<Permission> permissions, string updatedBy, CancellationToken ct = default);

    /// <summary>Delete a custom role (system roles cannot be deleted).</summary>
    Task DeleteRoleAsync(Guid roleId, string deletedBy, CancellationToken ct = default);

    // ── Permissions ────────────────────────────────────────────────────────

    /// <summary>Get all available permissions with display names.</summary>
    List<PermissionInfo> GetAllPermissions();

    /// <summary>Check if a role name has a specific permission.</summary>
    Task<bool> HasPermissionAsync(string tenantId, string roleName, Permission permission, CancellationToken ct = default);

    /// <summary>Get all permissions for a role name.</summary>
    Task<List<Permission>> GetPermissionsForRoleAsync(string tenantId, string roleName, CancellationToken ct = default);

    // ── Audit ──────────────────────────────────────────────────────────────

    /// <summary>Get audit trail entries for role/permission changes.</summary>
    Task<List<AuditLog>> GetRbacAuditLogAsync(string tenantId, int limit = 50, CancellationToken ct = default);

    // ── Team Role Assignment ───────────────────────────────────────────────

    /// <summary>Assign a role to a team member with audit logging.</summary>
    Task AssignRoleAsync(Guid memberId, string newRole, string changedBy, CancellationToken ct = default);
}

/// <summary>
/// Display info for a permission — name, description, category.
/// </summary>
public class PermissionInfo
{
    public Permission Permission { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}
