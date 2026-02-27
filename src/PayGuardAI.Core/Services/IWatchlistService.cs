using PayGuardAI.Core.Entities;

namespace PayGuardAI.Core.Services;

/// <summary>
/// Service for managing watchlists, blocklists, and allowlists.
/// Provides CRUD for lists and entries, bulk CSV import,
/// and a fast "check" method used by the risk scoring engine.
/// </summary>
public interface IWatchlistService
{
    // ── List CRUD ──────────────────────────────────────────────────────

    /// <summary>Get all watchlists for the current tenant.</summary>
    Task<List<Watchlist>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Get a single watchlist (with entries).</summary>
    Task<Watchlist?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Create a new watchlist.</summary>
    Task<Watchlist> CreateAsync(Watchlist watchlist, CancellationToken ct = default);

    /// <summary>Update list metadata (name, description, type, enabled).</summary>
    Task<Watchlist> UpdateAsync(Watchlist watchlist, CancellationToken ct = default);

    /// <summary>Delete a watchlist and all its entries.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    // ── Entry CRUD ─────────────────────────────────────────────────────

    /// <summary>Add a single entry to a watchlist.</summary>
    Task<WatchlistEntry> AddEntryAsync(Guid watchlistId, WatchlistEntry entry, CancellationToken ct = default);

    /// <summary>Remove an entry from a watchlist.</summary>
    Task RemoveEntryAsync(Guid entryId, CancellationToken ct = default);

    /// <summary>Bulk-add entries from CSV text (one value per line, or "value,fieldType,notes").</summary>
    Task<int> ImportEntriesFromCsvAsync(Guid watchlistId, string csvText, string defaultFieldType, string addedBy, CancellationToken ct = default);

    /// <summary>Remove all expired entries across all lists for the tenant.</summary>
    Task<int> PurgeExpiredEntriesAsync(CancellationToken ct = default);

    // ── Risk Scoring Integration ───────────────────────────────────────

    /// <summary>
    /// Check a transaction against all enabled watchlists for the tenant.
    /// Returns a list of <see cref="WatchlistHit"/> objects for any matches.
    /// Called by <see cref="IRiskScoringService"/> during analysis.
    /// </summary>
    Task<List<WatchlistHit>> CheckTransactionAsync(Transaction transaction, CancellationToken ct = default);
}

/// <summary>
/// A single watchlist match during risk scoring.
/// </summary>
public class WatchlistHit
{
    /// <summary>Which watchlist matched.</summary>
    public required string WatchlistName { get; set; }

    /// <summary>Blocklist / Watchlist / Allowlist.</summary>
    public WatchlistType ListType { get; set; }

    /// <summary>The field that was matched (e.g., "SourceCountry").</summary>
    public required string FieldType { get; set; }

    /// <summary>The actual value on the transaction that matched.</summary>
    public required string MatchedValue { get; set; }

    /// <summary>Notes from the entry (may be null).</summary>
    public string? EntryNotes { get; set; }

    /// <summary>Suggested risk score adjustment (positive = increase, negative = decrease).</summary>
    public int ScoreAdjustment { get; set; }
}
