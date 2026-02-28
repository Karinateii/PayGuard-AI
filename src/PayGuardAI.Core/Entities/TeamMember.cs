namespace PayGuardAI.Core.Entities;

/// <summary>
/// Team member within a tenant — name, role, invite status, MFA state.
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

    // ── MFA ──────────────────────────────────────────────────────
    /// <summary>Whether TOTP-based MFA is enabled for this member.</summary>
    public bool MfaEnabled { get; set; }

    /// <summary>Base-32 encoded TOTP secret (encrypted at rest via column-level config).</summary>
    public string MfaSecret { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated SHA-256 hashes of unused backup codes.
    /// Each code is hashed before storage; used codes are removed from this list.
    /// </summary>
    public string BackupCodeHashes { get; set; } = string.Empty;

    /// <summary>When MFA was enabled (null if never enabled).</summary>
    public DateTime? MfaEnabledAt { get; set; }
}
