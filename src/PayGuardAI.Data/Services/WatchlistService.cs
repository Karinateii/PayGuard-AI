using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Watchlist / Blocklist / Allowlist management and transaction checking.
/// All queries are tenant-scoped via the global query filter on <see cref="Watchlist"/>.
/// </summary>
public class WatchlistService : IWatchlistService
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<WatchlistService> _logger;

    public WatchlistService(
        ApplicationDbContext db,
        ITenantContext tenantContext,
        ILogger<WatchlistService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    // ── List CRUD ──────────────────────────────────────────────────────

    public async Task<List<Watchlist>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Watchlists
            .Include(w => w.Entries)
            .OrderBy(w => w.Name)
            .ToListAsync(ct);
    }

    public async Task<Watchlist?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Watchlists
            .Include(w => w.Entries.OrderByDescending(e => e.AddedAt))
            .FirstOrDefaultAsync(w => w.Id == id, ct);
    }

    public async Task<Watchlist> CreateAsync(Watchlist watchlist, CancellationToken ct = default)
    {
        watchlist.TenantId = _tenantContext.TenantId;
        watchlist.CreatedAt = DateTime.UtcNow;
        watchlist.UpdatedAt = DateTime.UtcNow;

        _db.Watchlists.Add(watchlist);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Watchlist created: {Name} ({Type}) by {User}",
            watchlist.Name, watchlist.ListType, watchlist.CreatedBy);

        return watchlist;
    }

    public async Task<Watchlist> UpdateAsync(Watchlist watchlist, CancellationToken ct = default)
    {
        var existing = await _db.Watchlists.FindAsync([watchlist.Id], ct)
            ?? throw new ArgumentException($"Watchlist {watchlist.Id} not found");

        existing.Name = watchlist.Name;
        existing.Description = watchlist.Description;
        existing.ListType = watchlist.ListType;
        existing.IsEnabled = watchlist.IsEnabled;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var watchlist = await _db.Watchlists
            .Include(w => w.Entries)
            .FirstOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new ArgumentException($"Watchlist {id} not found");

        _db.WatchlistEntries.RemoveRange(watchlist.Entries);
        _db.Watchlists.Remove(watchlist);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Watchlist deleted: {Name} ({EntryCount} entries removed)",
            watchlist.Name, watchlist.Entries.Count);
    }

    // ── Entry CRUD ─────────────────────────────────────────────────────

    public async Task<WatchlistEntry> AddEntryAsync(Guid watchlistId, WatchlistEntry entry, CancellationToken ct = default)
    {
        var watchlist = await _db.Watchlists.FindAsync([watchlistId], ct)
            ?? throw new ArgumentException($"Watchlist {watchlistId} not found");

        entry.WatchlistId = watchlistId;
        entry.AddedAt = DateTime.UtcNow;

        _db.WatchlistEntries.Add(entry);
        watchlist.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return entry;
    }

    public async Task RemoveEntryAsync(Guid entryId, CancellationToken ct = default)
    {
        var entry = await _db.WatchlistEntries.FindAsync([entryId], ct)
            ?? throw new ArgumentException($"Entry {entryId} not found");

        _db.WatchlistEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> ImportEntriesFromCsvAsync(
        Guid watchlistId, string csvText, string defaultFieldType, string addedBy, CancellationToken ct = default)
    {
        var watchlist = await _db.Watchlists.FindAsync([watchlistId], ct)
            ?? throw new ArgumentException($"Watchlist {watchlistId} not found");

        var lines = csvText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int count = 0;

        foreach (var line in lines)
        {
            // Skip header-looking lines
            if (line.StartsWith("value", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("#"))
                continue;

            var parts = line.Split(',', 3, StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
                continue;

            var entry = new WatchlistEntry
            {
                WatchlistId = watchlistId,
                Value = parts[0],
                FieldType = parts.Length >= 2 && WatchlistEntry.AllowedFields.ContainsKey(parts[1])
                    ? parts[1]
                    : defaultFieldType,
                Notes = parts.Length >= 3 ? parts[2] : null,
                AddedBy = addedBy,
                AddedAt = DateTime.UtcNow
            };

            _db.WatchlistEntries.Add(entry);
            count++;
        }

        if (count > 0)
        {
            watchlist.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Imported {Count} entries into watchlist {Name}", count, watchlist.Name);
        return count;
    }

    public async Task<int> PurgeExpiredEntriesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expired = await _db.WatchlistEntries
            .Where(e => e.ExpiresAt != null && e.ExpiresAt < now)
            .ToListAsync(ct);

        if (expired.Count > 0)
        {
            _db.WatchlistEntries.RemoveRange(expired);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Purged {Count} expired watchlist entries", expired.Count);
        }

        return expired.Count;
    }

    // ── Risk Scoring Integration ───────────────────────────────────────

    public async Task<List<WatchlistHit>> CheckTransactionAsync(
        Transaction transaction, CancellationToken ct = default)
    {
        var hits = new List<WatchlistHit>();

        // Load all enabled watchlists with non-expired entries
        var watchlists = await _db.Watchlists
            .Where(w => w.IsEnabled)
            .Include(w => w.Entries)
            .AsNoTracking()
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        foreach (var watchlist in watchlists)
        {
            foreach (var entry in watchlist.Entries)
            {
                // Skip expired entries
                if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < now)
                    continue;

                var txValue = GetTransactionFieldValue(entry.FieldType, transaction);
                if (txValue == null) continue;

                // Case-insensitive match
                if (!txValue.Equals(entry.Value, StringComparison.OrdinalIgnoreCase))
                    continue;

                // We have a match!
                var scoreAdj = watchlist.ListType switch
                {
                    WatchlistType.Blocklist => 35,   // Heavy penalty
                    WatchlistType.Watchlist => 15,    // Moderate flag
                    WatchlistType.Allowlist => -10,   // Trust reduction
                    _ => 0
                };

                hits.Add(new WatchlistHit
                {
                    WatchlistName = watchlist.Name,
                    ListType = watchlist.ListType,
                    FieldType = entry.FieldType,
                    MatchedValue = txValue,
                    EntryNotes = entry.Notes,
                    ScoreAdjustment = scoreAdj
                });
            }
        }

        if (hits.Count > 0)
        {
            _logger.LogInformation(
                "Transaction {TxId} matched {HitCount} watchlist entries: {Lists}",
                transaction.Id, hits.Count,
                string.Join(", ", hits.Select(h => $"{h.WatchlistName}({h.ListType})")));
        }

        return hits;
    }

    // ── Private Helpers ────────────────────────────────────────────────

    /// <summary>
    /// Extracts the value of a transaction field by name for matching against
    /// watchlist entries.
    /// </summary>
    private static string? GetTransactionFieldValue(string fieldType, Transaction transaction)
    {
        return fieldType switch
        {
            "SenderId"           => transaction.SenderId,
            "ReceiverId"         => transaction.ReceiverId,
            "SourceCountry"      => transaction.SourceCountry,
            "DestinationCountry" => transaction.DestinationCountry,
            "SourceCurrency"     => transaction.SourceCurrency,
            "DestinationCurrency"=> transaction.DestinationCurrency,
            _ => null
        };
    }
}
