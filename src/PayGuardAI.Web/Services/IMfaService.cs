using PayGuardAI.Web.Models;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Interface for Multi-Factor Authentication (TOTP) operations with persistence.
/// </summary>
public interface IMfaService
{
    /// <summary>Generate a new TOTP secret for a user.</summary>
    string GenerateSecret();

    /// <summary>Generate a QR code URI for authenticator app setup.</summary>
    string GenerateQrCodeUri(string userEmail, string secret);

    /// <summary>Validate a TOTP code against a secret.</summary>
    bool ValidateCode(string secret, string code);

    /// <summary>Generate backup codes for account recovery.</summary>
    string[] GenerateBackupCodes(int count = 10);

    /// <summary>Check if MFA is required for a user based on roles.</summary>
    bool IsMfaRequiredForUser(string[] userRoles);

    // ── Persistence methods ──────────────────────────────────────

    /// <summary>
    /// Enable MFA for a team member: stores the secret + hashed backup codes.
    /// Call this AFTER the user has verified the TOTP code during setup.
    /// </summary>
    Task EnableMfaAsync(Guid memberId, string secret, string[] backupCodes, CancellationToken ct = default);

    /// <summary>Disable MFA for a team member (clears secret and backup codes).</summary>
    Task DisableMfaAsync(Guid memberId, CancellationToken ct = default);

    /// <summary>Check whether MFA is enabled for a specific team member.</summary>
    Task<bool> IsMfaEnabledAsync(Guid memberId, CancellationToken ct = default);

    /// <summary>Load the MFA secret for a team member (empty string if not set).</summary>
    Task<string> GetMfaSecretAsync(Guid memberId, CancellationToken ct = default);

    /// <summary>
    /// Validate a backup code. Returns true and removes the code from the stored list if valid.
    /// </summary>
    Task<bool> ValidateBackupCodeAsync(Guid memberId, string code, CancellationToken ct = default);
}
