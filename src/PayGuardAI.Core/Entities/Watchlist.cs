namespace PayGuardAI.Core.Entities;

/// <summary>
/// A named list of entries used for blocking, watching, or allowing
/// specific values (countries, sender IDs, email domains, etc.)
/// during risk scoring.
/// </summary>
public class Watchlist
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Tenant this watchlist belongs to.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Human-readable list name (e.g., "High-Risk Countries", "Known Fraudsters").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description of the list's purpose.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// List type: Blocklist, Watchlist, or Allowlist.
    /// - Blocklist: auto-block (or heavily score) matching transactions.
    /// - Watchlist: flag for review when matched.
    /// - Allowlist: reduce risk score for trusted values.
    /// </summary>
    public WatchlistType ListType { get; set; } = WatchlistType.Blocklist;

    /// <summary>Whether the list is actively evaluated during risk scoring.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>When the list was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the list was last modified.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Who created the list.</summary>
    public string CreatedBy { get; set; } = "system";

    // Navigation
    public List<WatchlistEntry> Entries { get; set; } = [];
}

/// <summary>
/// The type / intent of a watchlist.
/// </summary>
public enum WatchlistType
{
    /// <summary>Blocked — transactions matching entries receive a heavy risk penalty.</summary>
    Blocklist,

    /// <summary>Watch — transactions matching entries are flagged for human review.</summary>
    Watchlist,

    /// <summary>Allow — transactions matching entries receive a risk reduction (trusted).</summary>
    Allowlist
}
