namespace PayGuardAI.Core.Services;

/// <summary>
/// Service for generating and validating magic link (passwordless) login tokens.
/// </summary>
public interface IMagicLinkService
{
    /// <summary>
    /// Creates a token and sends a magic link email to the given address.
    /// Returns true if the email was sent (or logged in dev mode).
    /// </summary>
    Task<bool> SendMagicLinkAsync(string email, string requestIp, CancellationToken ct = default);

    /// <summary>
    /// Validates a magic link token. Returns the associated email if valid, null otherwise.
    /// Marks the token as consumed so it can't be reused.
    /// </summary>
    Task<string?> ValidateTokenAsync(string token, CancellationToken ct = default);
}
