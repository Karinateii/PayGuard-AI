namespace PayGuardAI.Core.Entities;

/// <summary>
/// A single entry in a watchlist — represents a value to match against
/// a specific transaction field (country code, sender ID, etc.).
/// </summary>
public class WatchlistEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the parent watchlist.</summary>
    public Guid WatchlistId { get; set; }

    /// <summary>
    /// The value to match (e.g., "NG", "usr_abc123", "example.com").
    /// Case-insensitive matching is applied at evaluation time.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Which transaction/profile field this entry should match against.
    /// Must be one of the keys in <see cref="AllowedFields"/>.
    /// </summary>
    public string FieldType { get; set; } = "SenderId";

    /// <summary>Optional notes explaining why this entry was added.</summary>
    public string? Notes { get; set; }

    /// <summary>Who added this entry.</summary>
    public string AddedBy { get; set; } = "system";

    /// <summary>When the entry was added.</summary>
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional expiration — entry is ignored after this date.
    /// Null = never expires.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Whether this entry has expired.</summary>
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;

    // Navigation
    public Watchlist? Watchlist { get; set; }

    /// <summary>
    /// The transaction / profile fields that entries can target.
    /// Keys must align with field extraction in WatchlistService.
    /// </summary>
    public static readonly Dictionary<string, string> AllowedFields = new()
    {
        ["SenderId"]           = "Sender ID",
        ["ReceiverId"]         = "Receiver ID",
        ["SourceCountry"]      = "Source Country",
        ["DestinationCountry"] = "Destination Country",
        ["SourceCurrency"]     = "Source Currency",
        ["DestinationCurrency"]= "Destination Currency",
    };
}
