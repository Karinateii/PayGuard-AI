namespace PayGuardAI.Core.Entities;

/// <summary>
/// One-time login token sent via email for passwordless authentication.
/// </summary>
public class MagicLinkToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>SHA-256 hash of the actual token (never store plaintext).</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Email address the magic link was sent to.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>When the token expires (typically 15 minutes after creation).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Whether this token has already been consumed.</summary>
    public bool IsUsed { get; set; }

    /// <summary>IP address of the requester (for audit trail).</summary>
    public string RequestedFromIp { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
