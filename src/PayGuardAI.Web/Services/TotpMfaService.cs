using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OtpNet;
using PayGuardAI.Data;
using PayGuardAI.Web.Models;

namespace PayGuardAI.Web.Services;

/// <summary>
/// TOTP-based Multi-Factor Authentication service with database persistence.
/// Uses RFC 6238 Time-Based One-Time Password algorithm.
/// </summary>
public class TotpMfaService : IMfaService
{
    private readonly MfaSettings _settings;
    private readonly ILogger<TotpMfaService> _logger;
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public TotpMfaService(
        IOptions<MfaSettings> settings,
        ILogger<TotpMfaService> logger,
        IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _settings = settings.Value;
        _logger = logger;
        _dbFactory = dbFactory;
    }

    // ═══════════════════════════════════════════════════════════════
    // Pure TOTP operations (no DB)
    // ═══════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public string GenerateSecret()
    {
        var key = KeyGeneration.GenerateRandomKey(20);
        var secret = Base32Encoding.ToString(key);
        _logger.LogInformation("Generated new TOTP secret");
        return secret;
    }

    /// <inheritdoc />
    public string GenerateQrCodeUri(string userEmail, string secret)
    {
        if (string.IsNullOrWhiteSpace(userEmail))
            throw new ArgumentException("User email is required", nameof(userEmail));
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Secret is required", nameof(secret));

        var encodedIssuer = Uri.EscapeDataString(_settings.Issuer);
        var encodedAccount = Uri.EscapeDataString(userEmail);
        var encodedSecret = Uri.EscapeDataString(secret);

        return $"otpauth://totp/{encodedIssuer}:{encodedAccount}?" +
               $"secret={encodedSecret}&" +
               $"issuer={encodedIssuer}&" +
               $"algorithm=SHA1&" +
               $"digits=6&" +
               $"period={_settings.CodeValiditySeconds}";
    }

    /// <inheritdoc />
    public bool ValidateCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code) || code.Length != 6)
            return false;

        try
        {
            var secretBytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(secretBytes, step: _settings.CodeValiditySeconds);

            // Check current, previous, and next time window (±30 s tolerance)
            if (totp.ComputeTotp() == code)
                return true;
            if (totp.ComputeTotp(DateTime.UtcNow.AddSeconds(-_settings.CodeValiditySeconds)) == code)
                return true;
            if (totp.ComputeTotp(DateTime.UtcNow.AddSeconds(_settings.CodeValiditySeconds)) == code)
                return true;

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating TOTP code");
            return false;
        }
    }

    /// <inheritdoc />
    public string[] GenerateBackupCodes(int count = 10)
    {
        var codeCount = count > 0 ? count : _settings.BackupCodeCount;
        var codeLength = _settings.BackupCodeLength;
        var codes = new string[codeCount];
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        for (int i = 0; i < codeCount; i++)
        {
            var sb = new StringBuilder();
            var randomBytes = RandomNumberGenerator.GetBytes(codeLength);
            foreach (var b in randomBytes)
                sb.Append(chars[b % chars.Length]);

            var formatted = sb.ToString();
            codes[i] = formatted.Length == 8
                ? $"{formatted[..4]}-{formatted[4..]}"
                : formatted;
        }

        _logger.LogInformation("Generated {Count} backup codes", codeCount);
        return codes;
    }

    /// <inheritdoc />
    public bool IsMfaRequiredForUser(string[] userRoles)
    {
        if (_settings.EnforceMfaForAll)
            return true;
        if (userRoles is null || userRoles.Length == 0)
            return false;
        return userRoles.Any(r => _settings.RequiredMfaRoles.Contains(r, StringComparer.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════
    // Persistence methods
    // ═══════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task EnableMfaAsync(Guid memberId, string secret, string[] backupCodes, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var member = await db.TeamMembers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == memberId, ct);

        if (member is null)
        {
            _logger.LogWarning("EnableMfa: TeamMember {Id} not found", memberId);
            return;
        }

        member.MfaEnabled = true;
        member.MfaSecret = secret;
        member.MfaEnabledAt = DateTime.UtcNow;
        member.BackupCodeHashes = string.Join(",", backupCodes.Select(HashCode));

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("MFA enabled for {Email} ({Id})", member.Email, memberId);
    }

    /// <inheritdoc />
    public async Task DisableMfaAsync(Guid memberId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var member = await db.TeamMembers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == memberId, ct);

        if (member is null) return;

        member.MfaEnabled = false;
        member.MfaSecret = string.Empty;
        member.BackupCodeHashes = string.Empty;
        member.MfaEnabledAt = null;

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("MFA disabled for {Email} ({Id})", member.Email, memberId);
    }

    /// <inheritdoc />
    public async Task<bool> IsMfaEnabledAsync(Guid memberId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var member = await db.TeamMembers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == memberId, ct);
        return member?.MfaEnabled ?? false;
    }

    /// <inheritdoc />
    public async Task<string> GetMfaSecretAsync(Guid memberId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var member = await db.TeamMembers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == memberId, ct);
        return member?.MfaSecret ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<bool> ValidateBackupCodeAsync(Guid memberId, string code, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var member = await db.TeamMembers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == memberId, ct);

        if (member is null || string.IsNullOrEmpty(member.BackupCodeHashes))
            return false;

        var hashed = HashCode(code.Trim().ToUpperInvariant());
        var storedHashes = member.BackupCodeHashes.Split(',').ToList();

        if (!storedHashes.Contains(hashed))
            return false;

        // Remove the used code
        storedHashes.Remove(hashed);
        member.BackupCodeHashes = string.Join(",", storedHashes);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Backup code used for {Email} — {Remaining} codes remaining",
            member.Email, storedHashes.Count);
        return true;
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static string HashCode(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant()));
        return Convert.ToHexStringLower(bytes);
    }
}
