using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private readonly IHttpClientFactory _httpClientFactory;

    public MagicLinkService(
        ApplicationDbContext db,
        IConfiguration config,
        ILogger<MagicLinkService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
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

        // Magic links send via Resend HTTP API (SMTP ports are blocked on Railway).
        // Falls back to logging the URL if no API key is configured.
        var resendApiKey = _config["Email:ResendApiKey"] ?? _config["Email:SmtpPassword"] ?? "";

        if (!string.IsNullOrEmpty(resendApiKey) && resendApiKey.StartsWith("re_"))
        {
            // Use the configured from address, but fall back to Resend's shared
            // test domain which works immediately without domain verification.
            var configuredFrom = _config["Email:FromAddress"];
            var fromAddr = !string.IsNullOrEmpty(configuredFrom) ? configuredFrom : "onboarding@resend.dev";
            var fromName = _config["Email:FromName"] ?? "PayGuard AI";

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
                await SendViaResendApiAsync(resendApiKey, $"{fromName} <{fromAddr}>", email, "Sign in to PayGuard AI", html, ct);
                _logger.LogInformation("Magic link sent to {Email} via Resend API", email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send magic link email to {Email}. Link: {Url}", email, magicLinkUrl);
            }
        }
        else
        {
            // No API key configured: log the link so the developer can click it
            _logger.LogWarning("📧 MAGIC LINK (email not configured): {Url}", magicLinkUrl);
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
    /// Send email via Resend HTTP API (https://resend.com/docs/api-reference).
    /// Uses standard HTTPS (port 443) which works on Railway, unlike SMTP (port 587).
    /// </summary>
    private async Task SendViaResendApiAsync(
        string apiKey, string from, string to, string subject, string html, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            from,
            to = new[] { to },
            subject,
            html
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("https://api.resend.com/emails", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Resend API error {Status}: {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Resend API returned {response.StatusCode}: {body}");
        }

        _logger.LogDebug("Resend API response: {Body}", body);
    }
}
