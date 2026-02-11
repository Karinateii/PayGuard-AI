namespace PayGuardAI.Web.Models;

/// <summary>
/// Multi-Factor Authentication (TOTP) configuration settings
/// </summary>
public class MfaSettings
{
    /// <summary>
    /// Application name displayed in authenticator apps
    /// </summary>
    public string ApplicationName { get; set; } = "PayGuard AI";

    /// <summary>
    /// Issuer name displayed in authenticator apps
    /// </summary>
    public string Issuer { get; set; } = "PayGuard AI";

    /// <summary>
    /// TOTP code validity period in seconds
    /// </summary>
    public int CodeValiditySeconds { get; set; } = 30;

    /// <summary>
    /// Number of backup codes to generate
    /// </summary>
    public int BackupCodeCount { get; set; } = 10;

    /// <summary>
    /// Backup code length
    /// </summary>
    public int BackupCodeLength { get; set; } = 8;

    /// <summary>
    /// Whether to enforce MFA for all users
    /// </summary>
    public bool EnforceMfaForAll { get; set; } = false;

    /// <summary>
    /// Roles that require MFA (e.g., Admin, Manager)
    /// </summary>
    public string[] RequiredMfaRoles { get; set; } = ["Admin", "Manager"];

    /// <summary>
    /// Grace period after login before MFA is required (minutes)
    /// </summary>
    public int MfaGracePeriodMinutes { get; set; } = 0;

    /// <summary>
    /// Maximum failed MFA attempts before lockout
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>
    /// Lockout duration in minutes after max failed attempts
    /// </summary>
    public int LockoutDurationMinutes { get; set; } = 15;
}
