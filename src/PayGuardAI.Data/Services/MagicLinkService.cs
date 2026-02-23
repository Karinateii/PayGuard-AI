using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

public class MagicLinkService : IMagicLinkService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<MagicLinkService> _logger;

    public MagicLinkService(
        ApplicationDbContext db,
        IConfiguration config,
        ILogger<MagicLinkService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task<bool> SendMagicLinkAsync(string email, string requestIp, CancellationToken ct = default)
    {
        email = email.Trim().ToLowerInvariant();

        // Check the user actually exists in some org
        var teamMember = await _db.TeamMembers
            .IgnoreQueryFilters()
            .AnyAsync(t => t.Email == email && t.Status == "active", ct);

        if (!teamMember)
        {
            // Don't reveal whether the email exists — always show "check your email"
            _logger.LogWarning("Magic link requested for unknown email {Email}", email);
            return true;
        }

        // Rate limit: max 5 tokens per email in last 15 minutes
        var recentCount = await _db.MagicLinkTokens
            .Where(t => t.Email == email && t.CreatedAt > DateTime.UtcNow.AddMinutes(-15))
            .CountAsync(ct);

        if (recentCount >= 5)
        {
            _logger.LogWarning("Rate limit reached for magic link requests from {Email}", email);
            return true; // Still don't reveal — just silently skip
        }

        // Generate a cryptographically random token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes)
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        var tokenHash = HashToken(token);

        var expiryMinutes = int.TryParse(_config["MagicLink:TokenExpiryMinutes"], out var exp) ? exp : 15;

        _db.MagicLinkTokens.Add(new MagicLinkToken
        {
            TokenHash = tokenHash,
            Email = email,
            ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes),
            RequestedFromIp = requestIp
        });
        await _db.SaveChangesAsync(ct);

        // Build the magic link URL
        var appUrl = _config["AppUrl"]?.TrimEnd('/') ?? "http://localhost:5054";
        var magicLinkUrl = $"{appUrl}/api/Auth/magic-link/verify?token={Uri.EscapeDataString(token)}";

        // Magic links always send directly via SMTP (auth-critical, not optional notifications).
        // Only fall back to logging if SMTP is not configured at all.
        var smtpHost = _config["Email:SmtpHost"] ?? "";
        var smtpPass = _config["Email:SmtpPassword"] ?? "";

        if (!string.IsNullOrEmpty(smtpHost) && !string.IsNullOrEmpty(smtpPass))
        {
            var html = $"""
                <div style="font-family: -apple-system, BlinkMacSystemFont, sans-serif; max-width: 480px; margin: 0 auto; padding: 32px;">
                    <h2 style="color: #1a3a5c; margin-bottom: 24px;">Sign in to PayGuard AI</h2>
                    <p style="color: #555; line-height: 1.6;">Click the button below to sign in. This link expires in {expiryMinutes} minutes.</p>
                    <a href="{magicLinkUrl}"
                       style="display: inline-block; background: #1976d2; color: white; padding: 14px 32px;
                              border-radius: 8px; text-decoration: none; font-weight: 600; margin: 24px 0;">
                        Sign In
                    </a>
                    <p style="color: #999; font-size: 13px; margin-top: 24px;">
                        If you didn't request this, you can safely ignore this email.
                    </p>
                </div>
                """;

            try
            {
                await SendSmtpEmailAsync(email, "Sign in to PayGuard AI", html, ct);
                _logger.LogInformation("Magic link sent to {Email}", email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send magic link email to {Email}. Link: {Url}", email, magicLinkUrl);
            }
        }
        else
        {
            // No SMTP configured: log the link so the developer can click it
            _logger.LogWarning("📧 MAGIC LINK (SMTP not configured): {Url}", magicLinkUrl);
        }

        return true;
    }

    public async Task<string?> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        var tokenHash = HashToken(token);

        var record = await _db.MagicLinkTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (record is null)
        {
            _logger.LogWarning("Magic link token not found");
            return null;
        }

        if (record.IsUsed)
        {
            _logger.LogWarning("Magic link token already used for {Email}", record.Email);
            return null;
        }

        if (record.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Magic link token expired for {Email}", record.Email);
            return null;
        }

        // Mark as consumed
        record.IsUsed = true;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Magic link validated for {Email}", record.Email);
        return record.Email;
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Send email directly via SMTP — bypasses notification feature flag
    /// because magic links are auth-critical.
    /// </summary>
    private async Task SendSmtpEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken ct)
    {
        var smtpHost = _config["Email:SmtpHost"] ?? "smtp.resend.com";
        var smtpPort = int.TryParse(_config["Email:SmtpPort"], out var p) ? p : 587;
        var smtpUser = _config["Email:SmtpUser"] ?? "resend";
        var smtpPass = _config["Email:SmtpPassword"] ?? "";
        var fromAddr = _config["Email:FromAddress"] ?? "noreply@payguard.ai";
        var fromName = _config["Email:FromName"] ?? "PayGuard AI";

        using var message = new MailMessage();
        message.From = new MailAddress(fromAddr, fromName);
        message.To.Add(new MailAddress(toEmail));
        message.Subject = subject;
        message.Body = htmlBody;
        message.IsBodyHtml = true;

        using var client = new SmtpClient(smtpHost, smtpPort);
        client.Credentials = new NetworkCredential(smtpUser, smtpPass);
        client.EnableSsl = true;

        await client.SendMailAsync(message, ct);
    }
}
