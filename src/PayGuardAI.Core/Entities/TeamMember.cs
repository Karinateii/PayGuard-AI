namespace PayGuardAI.Core.Entities;

/// <summary>
/// Team member within a tenant — name, role, invite status.
/// </summary>
public class TeamMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = "Reviewer";  // Reviewer, Manager, Admin
    public string Status { get; set; } = "active";   // active, invited, disabled
    public DateTime? LastLoginAt { get; set; }
    public DateTime InvitedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
