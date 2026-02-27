using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Detects fan-out / fan-in money-laundering patterns by aggregating
/// sender → recipient relationships within configurable time windows.
/// </summary>
public class RelationshipAnalysisService : IRelationshipAnalysisService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<RelationshipAnalysisService> _logger;

    // ── Thresholds (configurable later via OrganizationSettings) ──
    private const int FanOutThreshold24h = 5;   // >5 unique recipients in 24 h
    private const int FanInThreshold24h  = 5;   // >5 unique senders   in 24 h
    private const int FanOutThreshold1h  = 3;   // >3 unique recipients in 1 h
    private const int FanInThreshold1h   = 3;

    // Score contributions
    private const int FanOutScore = 25;
    private const int FanInScore  = 20;
    private const int RapidBurstScore = 10;  // bonus if both fan-out AND fan-in

    public RelationshipAnalysisService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        ILogger<RelationshipAnalysisService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────
    //  Real-time scoring hook
    // ──────────────────────────────────────────────────────────────

    public async Task<List<RelationshipHit>> CheckTransactionAsync(
        Transaction transaction, CancellationToken ct = default)
    {
        var hits = new List<RelationshipHit>();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var now = transaction.CreatedAt;

        // ── Fan-out: how many unique recipients has this sender contacted? ──
        await CheckFanOutAsync(db, transaction.SenderId, now, TimeSpan.FromHours(24),
            FanOutThreshold24h, "24h", hits, ct);
        await CheckFanOutAsync(db, transaction.SenderId, now, TimeSpan.FromHours(1),
            FanOutThreshold1h, "1h", hits, ct);

        // ── Fan-in: how many unique senders have sent to this receiver? ──
        if (!string.IsNullOrEmpty(transaction.ReceiverId))
        {
            await CheckFanInAsync(db, transaction.ReceiverId, now, TimeSpan.FromHours(24),
                FanInThreshold24h, "24h", hits, ct);
            await CheckFanInAsync(db, transaction.ReceiverId, now, TimeSpan.FromHours(1),
                FanInThreshold1h, "1h", hits, ct);
        }

        // ── Rapid relay / mule detection ─────────────────────────────
        // If the sender also received money from multiple sources recently,
        // they may be a mule (receives then immediately sends out).
        if (hits.Any(h => h.PatternType == "FAN_OUT"))
        {
            var senderAsReceiver = await db.Transactions
                .Where(t => t.ReceiverId == transaction.SenderId
                         && t.CreatedAt >= now.AddHours(-24))
                .Select(t => t.SenderId)
                .Distinct()
                .CountAsync(ct);

            if (senderAsReceiver >= 3)
            {
                hits.Add(new RelationshipHit
                {
                    PatternType = "MULE_RELAY",
                    Actor = transaction.SenderId,
                    UniqueCounterparties = senderAsReceiver,
                    TimeWindowLabel = "24h",
                    Threshold = 3,
                    ScoreAdjustment = RapidBurstScore,
                    Description = $"Possible mule: sender \"{Truncate(transaction.SenderId)}\" received from " +
                                  $"{senderAsReceiver} unique sources AND is fanning out — relay pattern detected"
                });
            }
        }

        return hits;
    }

    // ──────────────────────────────────────────────────────────────
    //  Relationship graph for UI visualisation
    // ──────────────────────────────────────────────────────────────

    public async Task<RelationshipGraph> GetRelationshipGraphAsync(
        string customerId, TimeWindow window, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var cutoff = CutoffFor(window);

        // Transactions where customer is sender
        var outgoing = await db.Transactions
            .Where(t => t.SenderId == customerId && t.CreatedAt >= cutoff && t.ReceiverId != null)
            .GroupBy(t => t.ReceiverId!)
            .Select(g => new
            {
                CounterpartyId = g.Key,
                Count = g.Count(),
                Total = g.Sum(t => t.Amount),
                First = g.Min(t => t.CreatedAt),
                Last = g.Max(t => t.CreatedAt)
            })
            .ToListAsync(ct);

        // Transactions where customer is receiver
        var incoming = await db.Transactions
            .Where(t => t.ReceiverId == customerId && t.CreatedAt >= cutoff)
            .GroupBy(t => t.SenderId)
            .Select(g => new
            {
                CounterpartyId = g.Key,
                Count = g.Count(),
                Total = g.Sum(t => t.Amount),
                First = g.Min(t => t.CreatedAt),
                Last = g.Max(t => t.CreatedAt)
            })
            .ToListAsync(ct);

        var graph = new RelationshipGraph
        {
            FocalCustomerId = customerId,
            TimeWindowLabel = LabelFor(window),
            FanOutCount = outgoing.Count,
            FanInCount = incoming.Count
        };

        // Focal node
        graph.Nodes.Add(new GraphNode
        {
            Id = customerId,
            Label = Truncate(customerId),
            Role = outgoing.Count > 0 && incoming.Count > 0 ? "both" : outgoing.Count > 0 ? "sender" : "receiver",
            TransactionCount = outgoing.Sum(o => o.Count) + incoming.Sum(i => i.Count),
            TotalAmount = outgoing.Sum(o => o.Total) + incoming.Sum(i => i.Total),
            IsFocal = true
        });

        // Outgoing counterparties
        foreach (var o in outgoing)
        {
            if (!graph.Nodes.Any(n => n.Id == o.CounterpartyId))
            {
                graph.Nodes.Add(new GraphNode
                {
                    Id = o.CounterpartyId,
                    Label = Truncate(o.CounterpartyId),
                    Role = "receiver",
                    TransactionCount = o.Count,
                    TotalAmount = o.Total
                });
            }
            graph.Edges.Add(new GraphEdge
            {
                Source = customerId,
                Target = o.CounterpartyId,
                Count = o.Count,
                TotalAmount = o.Total,
                FirstSeen = o.First,
                LastSeen = o.Last
            });
        }

        // Incoming counterparties
        foreach (var i in incoming)
        {
            if (!graph.Nodes.Any(n => n.Id == i.CounterpartyId))
            {
                graph.Nodes.Add(new GraphNode
                {
                    Id = i.CounterpartyId,
                    Label = Truncate(i.CounterpartyId),
                    Role = "sender",
                    TransactionCount = i.Count,
                    TotalAmount = i.Total
                });
            }
            graph.Edges.Add(new GraphEdge
            {
                Source = i.CounterpartyId,
                Target = customerId,
                Count = i.Count,
                TotalAmount = i.Total,
                FirstSeen = i.First,
                LastSeen = i.Last
            });
        }

        return graph;
    }

    // ──────────────────────────────────────────────────────────────
    //  Network summary (dashboard)
    // ──────────────────────────────────────────────────────────────

    public async Task<NetworkAnalysisSummary> GetNetworkSummaryAsync(
        TimeWindow window, int topN = 10, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var cutoff = CutoffFor(window);

        var txns = db.Transactions.Where(t => t.CreatedAt >= cutoff);

        // Fan-out: group by sender, count unique receivers
        var fanOutRaw = await txns
            .Where(t => t.ReceiverId != null)
            .GroupBy(t => t.SenderId)
            .Select(g => new
            {
                CustomerId = g.Key,
                UniqueRecipients = g.Select(t => t.ReceiverId!).Distinct().Count(),
                TxnCount = g.Count(),
                Total = g.Sum(t => t.Amount)
            })
            .OrderByDescending(x => x.UniqueRecipients)
            .Take(topN)
            .ToListAsync(ct);

        // Fan-in: group by receiver, count unique senders
        var fanInRaw = await txns
            .Where(t => t.ReceiverId != null)
            .GroupBy(t => t.ReceiverId!)
            .Select(g => new
            {
                CustomerId = g.Key,
                UniqueSenders = g.Select(t => t.SenderId).Distinct().Count(),
                TxnCount = g.Count(),
                Total = g.Sum(t => t.Amount)
            })
            .OrderByDescending(x => x.UniqueSenders)
            .Take(topN)
            .ToListAsync(ct);

        var totalRelationships = await txns
            .Where(t => t.ReceiverId != null)
            .Select(t => new { t.SenderId, t.ReceiverId })
            .Distinct()
            .CountAsync(ct);

        var totalTxns = await txns.CountAsync(ct);

        return new NetworkAnalysisSummary
        {
            TimeWindowLabel = LabelFor(window),
            TotalUniqueRelationships = totalRelationships,
            TotalTransactions = totalTxns,
            TopFanOut = fanOutRaw.Select(x => new ActorSummary
            {
                CustomerId = x.CustomerId,
                UniqueCounterparties = x.UniqueRecipients,
                TransactionCount = x.TxnCount,
                TotalAmount = x.Total,
                AverageAmount = x.TxnCount > 0 ? x.Total / x.TxnCount : 0,
                IsSuspicious = x.UniqueRecipients >= FanOutThreshold24h
            }).ToList(),
            TopFanIn = fanInRaw.Select(x => new ActorSummary
            {
                CustomerId = x.CustomerId,
                UniqueCounterparties = x.UniqueSenders,
                TransactionCount = x.TxnCount,
                TotalAmount = x.Total,
                AverageAmount = x.TxnCount > 0 ? x.Total / x.TxnCount : 0,
                IsSuspicious = x.UniqueSenders >= FanInThreshold24h
            }).ToList()
        };
    }

    // ──────────────────────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────────────────────

    private static async Task CheckFanOutAsync(
        ApplicationDbContext db, string senderId, DateTime now,
        TimeSpan window, int threshold, string windowLabel,
        List<RelationshipHit> hits, CancellationToken ct)
    {
        var cutoff = now - window;

        var stats = await db.Transactions
            .Where(t => t.SenderId == senderId
                     && t.CreatedAt >= cutoff
                     && t.ReceiverId != null)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                UniqueRecipients = g.Select(t => t.ReceiverId!).Distinct().Count(),
                TxnCount = g.Count(),
                Total = g.Sum(t => t.Amount)
            })
            .FirstOrDefaultAsync(ct);

        if (stats != null && stats.UniqueRecipients > threshold)
        {
            hits.Add(new RelationshipHit
            {
                PatternType = "FAN_OUT",
                Actor = senderId,
                UniqueCounterparties = stats.UniqueRecipients,
                TransactionCount = stats.TxnCount,
                TotalAmount = stats.Total,
                TimeWindowLabel = windowLabel,
                Threshold = threshold,
                ScoreAdjustment = FanOutScore,
                Description = $"Fan-out: sender \"{Truncate(senderId)}\" sent to " +
                              $"{stats.UniqueRecipients} unique recipients in {windowLabel} " +
                              $"(threshold: {threshold}) — total {stats.Total:C}"
            });
        }
    }

    private static async Task CheckFanInAsync(
        ApplicationDbContext db, string receiverId, DateTime now,
        TimeSpan window, int threshold, string windowLabel,
        List<RelationshipHit> hits, CancellationToken ct)
    {
        var cutoff = now - window;

        var stats = await db.Transactions
            .Where(t => t.ReceiverId == receiverId
                     && t.CreatedAt >= cutoff)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                UniqueSenders = g.Select(t => t.SenderId).Distinct().Count(),
                TxnCount = g.Count(),
                Total = g.Sum(t => t.Amount)
            })
            .FirstOrDefaultAsync(ct);

        if (stats != null && stats.UniqueSenders > threshold)
        {
            hits.Add(new RelationshipHit
            {
                PatternType = "FAN_IN",
                Actor = receiverId,
                UniqueCounterparties = stats.UniqueSenders,
                TransactionCount = stats.TxnCount,
                TotalAmount = stats.Total,
                TimeWindowLabel = windowLabel,
                Threshold = threshold,
                ScoreAdjustment = FanInScore,
                Description = $"Fan-in: receiver \"{Truncate(receiverId)}\" received from " +
                              $"{stats.UniqueSenders} unique senders in {windowLabel} " +
                              $"(threshold: {threshold}) — total {stats.Total:C}"
            });
        }
    }

    private static DateTime CutoffFor(TimeWindow w) => DateTime.UtcNow.Add(w switch
    {
        TimeWindow.OneHour => TimeSpan.FromHours(-1),
        TimeWindow.TwentyFourHours => TimeSpan.FromHours(-24),
        TimeWindow.SevenDays => TimeSpan.FromDays(-7),
        TimeWindow.ThirtyDays => TimeSpan.FromDays(-30),
        _ => TimeSpan.FromHours(-24)
    });

    private static string LabelFor(TimeWindow w) => w switch
    {
        TimeWindow.OneHour => "1h",
        TimeWindow.TwentyFourHours => "24h",
        TimeWindow.SevenDays => "7d",
        TimeWindow.ThirtyDays => "30d",
        _ => "24h"
    };

    private static string Truncate(string id) =>
        id.Length > 16 ? id[..8] + "…" + id[^4..] : id;
}
