namespace PayGuardAI.Core.Entities;

/// <summary>
/// Granular permissions for controlling access to PayGuard AI features.
/// Each permission maps to a specific capability in the application.
/// </summary>
public enum Permission
{
    // Transaction operations
    ViewTransactions,
    ReviewTransactions,
    
    // Risk rule management
    ManageRules,
    
    // Reporting & analytics
    ViewReports,
    ViewAuditLog,
    
    // Team & role management
    ManageTeam,
    ManageRoles,
    
    // System configuration
    ManageApiKeys,
    ManageWebhooks,
    ManageSettings,
    ManageBilling,
    ManageNotifications
}

/// <summary>
/// A custom role with a set of granular permissions.
/// Extends beyond the default Reviewer/Manager/Admin roles.
/// </summary>
public class CustomRole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>Tenant that owns this role.</summary>
    public string TenantId { get; set; } = string.Empty;
    
    /// <summary>Display name (e.g., "Senior Analyst", "Compliance Officer").</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>Explanation of what this role is for.</summary>
    public string? Description { get; set; }
    
    /// <summary>Whether this is a system-defined role (Reviewer/Manager/Admin).</summary>
    public bool IsSystem { get; set; }
    
    /// <summary>
    /// Comma-separated list of Permission enum values.
    /// Stored as string for SQLite compatibility.
    /// </summary>
    public string Permissions { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    
    // ── Helpers ───────────────────────────────────────────────────────────
    
    /// <summary>Parse the comma-separated Permissions string into a list.</summary>
    public List<Permission> GetPermissions()
    {
        if (string.IsNullOrWhiteSpace(Permissions)) return [];
        return Permissions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => Enum.TryParse<Permission>(p, out _))
            .Select(p => Enum.Parse<Permission>(p))
            .ToList();
    }
    
    /// <summary>Set permissions from a list of Permission enums.</summary>
    public void SetPermissions(IEnumerable<Permission> permissions)
    {
        Permissions = string.Join(",", permissions.Distinct().OrderBy(p => p));
    }
    
    /// <summary>Check if this role has a specific permission.</summary>
    public bool HasPermission(Permission permission)
        => GetPermissions().Contains(permission);

    // ── Built-in Role Definitions ─────────────────────────────────────────

    public static readonly Permission[] ReviewerPermissions =
    [
        Permission.ViewTransactions,
        Permission.ReviewTransactions,
        Permission.ViewReports
    ];

    public static readonly Permission[] ManagerPermissions =
    [
        Permission.ViewTransactions,
        Permission.ReviewTransactions,
        Permission.ManageRules,
        Permission.ViewReports,
        Permission.ViewAuditLog,
        Permission.ManageTeam,
        Permission.ManageNotifications
    ];

    public static readonly Permission[] AdminPermissions =
        Enum.GetValues<Permission>().ToArray();
}
