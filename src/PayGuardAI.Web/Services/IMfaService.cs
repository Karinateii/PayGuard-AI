using PayGuardAI.Web.Models;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Interface for Multi-Factor Authentication (TOTP) operations
/// </summary>
public interface IMfaService
{
    /// <summary>
    /// Generate a new TOTP secret for a user
    /// </summary>
    string GenerateSecret();

    /// <summary>
    /// Generate a QR code URI for authenticator app setup
    /// </summary>
    string GenerateQrCodeUri(string userEmail, string secret);

    /// <summary>
    /// Validate a TOTP code against a secret
    /// </summary>
    bool ValidateCode(string secret, string code);

    /// <summary>
    /// Generate backup codes for account recovery
    /// </summary>
    string[] GenerateBackupCodes(int count = 10);

    /// <summary>
    /// Check if MFA is required for a user based on roles
    /// </summary>
    bool IsMfaRequiredForUser(string[] userRoles);
}
