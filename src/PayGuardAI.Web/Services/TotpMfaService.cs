using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OtpNet;
using PayGuardAI.Web.Models;

namespace PayGuardAI.Web.Services;

/// <summary>
/// TOTP-based Multi-Factor Authentication service
/// Uses RFC 6238 Time-Based One-Time Password algorithm
/// </summary>
public class TotpMfaService : IMfaService
{
    private readonly MfaSettings _settings;
    private readonly ILogger<TotpMfaService> _logger;

    public TotpMfaService(
        IOptions<MfaSettings> settings,
        ILogger<TotpMfaService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Generate a new cryptographically secure TOTP secret
    /// </summary>
    public string GenerateSecret()
    {
        // Generate 20 random bytes (160 bits) for RFC 6238 compliance
        var key = KeyGeneration.GenerateRandomKey(20);
        var secret = Base32Encoding.ToString(key);
        
        _logger.LogInformation("Generated new TOTP secret");
        return secret;
    }

    /// <summary>
    /// Generate otpauth:// URI for QR code generation
    /// Compatible with Google Authenticator, Microsoft Authenticator, Authy, etc.
    /// </summary>
    public string GenerateQrCodeUri(string userEmail, string secret)
    {
        if (string.IsNullOrWhiteSpace(userEmail))
            throw new ArgumentException("User email is required", nameof(userEmail));
        
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Secret is required", nameof(secret));

        // Encode for URI (RFC 3986)
        var encodedIssuer = Uri.EscapeDataString(_settings.Issuer);
        var encodedAccount = Uri.EscapeDataString(userEmail);
        var encodedSecret = Uri.EscapeDataString(secret);

        var uri = $"otpauth://totp/{encodedIssuer}:{encodedAccount}?" +
                  $"secret={encodedSecret}&" +
                  $"issuer={encodedIssuer}&" +
                  $"algorithm=SHA1&" +
                  $"digits=6&" +
                  $"period={_settings.CodeValiditySeconds}";

        _logger.LogDebug("Generated QR code URI for user {UserEmail}", userEmail);
        return uri;
    }

    /// <summary>
    /// Validate a TOTP code with time window tolerance
    /// Accepts codes from previous, current, and next time windows (±30s tolerance)
    /// </summary>
    public bool ValidateCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning("Validation attempted with empty secret");
            return false;
        }

        if (string.IsNullOrWhiteSpace(code) || code.Length != 6)
        {
            _logger.LogWarning("Invalid code format: {CodeLength} characters", code?.Length ?? 0);
            return false;
        }

        try
        {
            var secretBytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(secretBytes, step: _settings.CodeValiditySeconds);

            // Check current time window
            var currentCode = totp.ComputeTotp();
            if (currentCode == code)
            {
                _logger.LogInformation("TOTP code validated successfully (current window)");
                return true;
            }

            // Check previous time window (tolerance for clock skew)
            var previousCode = totp.ComputeTotp(DateTime.UtcNow.AddSeconds(-_settings.CodeValiditySeconds));
            if (previousCode == code)
            {
                _logger.LogInformation("TOTP code validated successfully (previous window)");
                return true;
            }

            // Check next time window (tolerance for clock skew)
            var nextCode = totp.ComputeTotp(DateTime.UtcNow.AddSeconds(_settings.CodeValiditySeconds));
            if (nextCode == code)
            {
                _logger.LogInformation("TOTP code validated successfully (next window)");
                return true;
            }

            _logger.LogWarning("TOTP code validation failed");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating TOTP code");
            return false;
        }
    }

    /// <summary>
    /// Generate cryptographically secure backup codes
    /// Format: XXXX-XXXX for 8-character codes
    /// </summary>
    public string[] GenerateBackupCodes(int count = 10)
    {
        var codeCount = count > 0 ? count : _settings.BackupCodeCount;
        var codeLength = _settings.BackupCodeLength;
        var codes = new string[codeCount];

        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        
        for (int i = 0; i < codeCount; i++)
        {
            var code = new StringBuilder();
            var randomBytes = RandomNumberGenerator.GetBytes(codeLength);
            
            foreach (var b in randomBytes)
            {
                code.Append(chars[b % chars.Length]);
            }

            // Format: XXXX-XXXX for readability
            var formatted = code.ToString();
            if (formatted.Length == 8)
            {
                formatted = $"{formatted.Substring(0, 4)}-{formatted.Substring(4, 4)}";
            }

            codes[i] = formatted;
        }

        _logger.LogInformation("Generated {Count} backup codes", codeCount);
        return codes;
    }

    /// <summary>
    /// Determine if MFA is required based on user roles and settings
    /// </summary>
    public bool IsMfaRequiredForUser(string[] userRoles)
    {
        if (_settings.EnforceMfaForAll)
        {
            _logger.LogDebug("MFA required: EnforceMfaForAll is enabled");
            return true;
        }

        if (userRoles == null || userRoles.Length == 0)
        {
            _logger.LogDebug("MFA not required: No roles assigned");
            return false;
        }

        var requiresMfa = userRoles.Any(role => 
            _settings.RequiredMfaRoles.Contains(role, StringComparer.OrdinalIgnoreCase));

        if (requiresMfa)
        {
            _logger.LogDebug("MFA required: User has role requiring MFA");
        }
        else
        {
            _logger.LogDebug("MFA not required: User roles do not require MFA");
        }

        return requiresMfa;
    }
}
