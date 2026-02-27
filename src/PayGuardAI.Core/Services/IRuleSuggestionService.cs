namespace PayGuardAI.Core.Services;

/// <summary>
/// AI-powered rule suggestion engine.
/// Analyzes recent transaction patterns (flagged, blocked, high-risk) and
/// proposes new expression rules that the compliance team can adopt with one click.
/// </summary>
public interface IRuleSuggestionService
{
    /// <summary>
    /// Analyze recent transaction patterns and generate rule suggestions.
    /// </summary>
    /// <param name="tenantId">Tenant to scope the analysis to.</param>
    /// <param name="lookbackDays">Number of days to look back (default 30).</param>
    /// <returns>List of rule suggestions ordered by confidence descending.</returns>
    Task<List<RuleSuggestion>> GenerateSuggestionsAsync(string tenantId, int lookbackDays = 30);
}

// ── DTOs ─────────────────────────────────────────────────────────────

/// <summary>
/// A single rule suggestion with pre-filled expression fields and confidence score.
/// </summary>
public class RuleSuggestion
{
    /// <summary>Unique key so the UI can dismiss/track suggestions.</summary>
    public string SuggestionId { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Suggested rule name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable explanation of why this rule is suggested.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Category: Amount, Velocity, Geography, Pattern.</summary>
    public string Category { get; set; } = "Pattern";

    /// <summary>The ExpressionField key (e.g., "Amount", "SourceCountry").</summary>
    public string ExpressionField { get; set; } = string.Empty;

    /// <summary>The comparison operator (e.g., ">=", "==").</summary>
    public string ExpressionOperator { get; set; } = string.Empty;

    /// <summary>The threshold / value (e.g., "7500", "NG").</summary>
    public string ExpressionValue { get; set; } = string.Empty;

    /// <summary>Suggested risk-score weight (1-50).</summary>
    public int SuggestedWeight { get; set; } = 15;

    /// <summary>Confidence percentage 0-100. Higher = stronger signal.</summary>
    public int Confidence { get; set; }

    /// <summary>The pattern type that generated this suggestion.</summary>
    public SuggestionPattern Pattern { get; set; }

    /// <summary>Supporting evidence (e.g., "42 flagged txns matched this pattern").</summary>
    public string Evidence { get; set; } = string.Empty;
}

/// <summary>
/// The type of pattern analysis that generated the suggestion.
/// </summary>
public enum SuggestionPattern
{
    /// <summary>High-value cluster: many flagged txns share a similar amount range.</summary>
    AmountCluster,

    /// <summary>Corridor hotspot: a specific source→destination pair has high flag rate.</summary>
    CorridorHotspot,

    /// <summary>Time-of-day anomaly: flagged txns cluster in certain hours.</summary>
    TimeAnomaly,

    /// <summary>Round-amount spike: unusual concentration of round amounts.</summary>
    RoundAmountSpike,

    /// <summary>Velocity burst: customers with many txns in short windows.</summary>
    VelocityBurst,

    /// <summary>Currency pair risk: specific currency combinations show high risk.</summary>
    CurrencyPairRisk,

    /// <summary>New-customer risk: recently onboarded customers triggering flags.</summary>
    NewCustomerRisk,

    /// <summary>Repeat offender: customers with multiple flagged transactions.</summary>
    RepeatOffender
}
