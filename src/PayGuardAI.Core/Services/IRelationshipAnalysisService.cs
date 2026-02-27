using PayGuardAI.Core.Entities;

namespace PayGuardAI.Core.Services;

/// <summary>
/// Detects fan-out (one sender → many recipients) and fan-in (many senders → one recipient)
/// patterns that indicate money laundering or mule-account activity.
/// </summary>
public interface IRelationshipAnalysisService
{
    /// <summary>
    /// Analyse a transaction for fan-out / fan-in patterns and return any hits.
    /// Called from RiskScoringService during real-time scoring.
    /// </summary>
    Task<List<RelationshipHit>> CheckTransactionAsync(Transaction transaction, CancellationToken ct = default);

    /// <summary>
    /// Build a full relationship graph for a given customer (sender or receiver)
    /// within the requested time window.  Used by the UI visualisation page.
    /// </summary>
    Task<RelationshipGraph> GetRelationshipGraphAsync(string customerId, TimeWindow window, CancellationToken ct = default);

    /// <summary>
    /// Return the top fan-out and fan-in actors across the tenant for a time window.
    /// Powers the Network Analysis dashboard.
    /// </summary>
    Task<NetworkAnalysisSummary> GetNetworkSummaryAsync(TimeWindow window, int topN = 10, CancellationToken ct = default);
}

// ────────────────────────────────────────────────────────────────
//  DTOs
// ────────────────────────────────────────────────────────────────

/// <summary>Time windows used for relationship aggregation.</summary>
public enum TimeWindow
{
    OneHour,
    TwentyFourHours,
    SevenDays,
    ThirtyDays
}

/// <summary>A fan-out or fan-in hit produced during scoring.</summary>
public class RelationshipHit
{
    public string PatternType { get; set; } = string.Empty;   // "FAN_OUT" or "FAN_IN"
    public string Actor { get; set; } = string.Empty;          // The sender (fan-out) or receiver (fan-in)
    public int UniqueCounterparties { get; set; }
    public decimal TotalAmount { get; set; }
    public int TransactionCount { get; set; }
    public string TimeWindowLabel { get; set; } = string.Empty;  // "24h", "1h", etc.
    public int Threshold { get; set; }
    public int ScoreAdjustment { get; set; }
    public string Description { get; set; } = string.Empty;
}

/// <summary>Node in a relationship graph.</summary>
public class GraphNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;   // "sender", "receiver", "both"
    public int TransactionCount { get; set; }
    public decimal TotalAmount { get; set; }
    public bool IsFocal { get; set; }
}

/// <summary>Edge (relationship) in a relationship graph.</summary>
public class GraphEdge
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
}

/// <summary>Complete relationship graph for a customer.</summary>
public class RelationshipGraph
{
    public string FocalCustomerId { get; set; } = string.Empty;
    public string TimeWindowLabel { get; set; } = string.Empty;
    public List<GraphNode> Nodes { get; set; } = new();
    public List<GraphEdge> Edges { get; set; } = new();
    public int FanOutCount { get; set; }
    public int FanInCount { get; set; }
}

/// <summary>Top-N fan-out/fan-in summary for the tenant.</summary>
public class NetworkAnalysisSummary
{
    public string TimeWindowLabel { get; set; } = string.Empty;
    public List<ActorSummary> TopFanOut { get; set; } = new();
    public List<ActorSummary> TopFanIn { get; set; } = new();
    public int TotalUniqueRelationships { get; set; }
    public int TotalTransactions { get; set; }
}

/// <summary>An actor's relationship summary.</summary>
public class ActorSummary
{
    public string CustomerId { get; set; } = string.Empty;
    public int UniqueCounterparties { get; set; }
    public int TransactionCount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AverageAmount { get; set; }
    public bool IsSuspicious { get; set; }
}
