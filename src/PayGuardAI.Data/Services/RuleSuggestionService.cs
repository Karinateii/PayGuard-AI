using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// AI-powered rule suggestion engine that analyzes recent transaction patterns
/// (flagged, blocked, high-risk) and proposes new expression rules.
/// Uses statistical clustering on the last N days of risk data.
/// </summary>
public class RuleSuggestionService : IRuleSuggestionService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<RuleSuggestionService> _logger;

    // Minimum sample sizes to generate a suggestion
    private const int MinFlaggedSample = 5;
    private const int MinPatternOccurrences = 3;

    public RuleSuggestionService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        ILogger<RuleSuggestionService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<List<RuleSuggestion>> GenerateSuggestionsAsync(string tenantId, int lookbackDays = 30)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.SetTenantId(tenantId);
        var cutoff = DateTime.UtcNow.AddDays(-lookbackDays);

        // ── Load flagged/high-risk transactions with their risk analyses ──
        var flaggedTxns = await db.Transactions
            .Where(t => t.TenantId == tenantId && t.CreatedAt >= cutoff)
            .Join(db.RiskAnalyses.Where(ra =>
                    ra.TenantId == tenantId &&
                    ra.RiskLevel >= RiskLevel.Medium),
                t => t.Id, ra => ra.TransactionId,
                (t, ra) => new FlaggedTransaction
                {
                    Transaction = t,
                    RiskScore = ra.RiskScore,
                    RiskLevel = ra.RiskLevel
                })
            .ToListAsync();

        if (flaggedTxns.Count < MinFlaggedSample)
        {
            _logger.LogInformation(
                "Tenant {TenantId}: only {Count} flagged txns in {Days}d — too few for suggestions",
                tenantId, flaggedTxns.Count, lookbackDays);
            return [];
        }

        // ── Load all transactions for baseline comparison ──
        var totalTxnCount = await db.Transactions
            .Where(t => t.TenantId == tenantId && t.CreatedAt >= cutoff)
            .CountAsync();

        // ── Load existing rules so we don't suggest duplicates ──
        var existingRules = await db.RiskRules
            .Where(r => r.TenantId == tenantId || r.TenantId == "")
            .ToListAsync();

        var suggestions = new List<RuleSuggestion>();

        // Run all pattern detectors
        suggestions.AddRange(DetectAmountClusters(flaggedTxns, totalTxnCount, existingRules));
        suggestions.AddRange(DetectCorridorHotspots(flaggedTxns, totalTxnCount, existingRules));
        suggestions.AddRange(DetectTimeAnomalies(flaggedTxns, totalTxnCount, existingRules));
        suggestions.AddRange(DetectRoundAmountSpikes(flaggedTxns, totalTxnCount, existingRules));
        suggestions.AddRange(DetectCurrencyPairRisks(flaggedTxns, totalTxnCount, existingRules));
        suggestions.AddRange(await DetectVelocityBurstsAsync(db, tenantId, cutoff, existingRules));
        suggestions.AddRange(await DetectRepeatOffendersAsync(db, tenantId, cutoff, existingRules));
        suggestions.AddRange(await DetectNewCustomerRiskAsync(db, tenantId, cutoff, flaggedTxns, existingRules));

        // Deduplicate by field+operator+value, keep highest confidence
        suggestions = suggestions
            .GroupBy(s => $"{s.ExpressionField}|{s.ExpressionOperator}|{s.ExpressionValue}")
            .Select(g => g.OrderByDescending(s => s.Confidence).First())
            .OrderByDescending(s => s.Confidence)
            .Take(10) // Cap at 10 suggestions
            .ToList();

        _logger.LogInformation(
            "Tenant {TenantId}: generated {Count} rule suggestions from {Flagged}/{Total} flagged txns ({Days}d)",
            tenantId, suggestions.Count, flaggedTxns.Count, totalTxnCount, lookbackDays);

        return suggestions;
    }

    // ══════════════════════════════════════════════════════════════
    //  Pattern Detectors
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Detect clusters of flagged transactions around specific amount thresholds.
    /// If many flagged txns share a similar amount range, suggest a threshold rule.
    /// </summary>
    private List<RuleSuggestion> DetectAmountClusters(
        List<FlaggedTransaction> flagged, int totalCount, List<RiskRule> existing)
    {
        var results = new List<RuleSuggestion>();
        if (flagged.Count < MinPatternOccurrences) return results;

        var amounts = flagged.Select(f => f.Transaction.Amount).OrderBy(a => a).ToList();

        // Find the 75th-percentile amount of flagged transactions
        var p75Index = (int)(amounts.Count * 0.75);
        var p75Amount = amounts[Math.Min(p75Index, amounts.Count - 1)];

        // Round to a clean threshold (nearest 500 or 1000)
        var threshold = RoundToCleanThreshold(p75Amount);

        // Count how many flagged txns would be caught by this threshold
        var matchCount = flagged.Count(f => f.Transaction.Amount >= threshold);
        if (matchCount < MinPatternOccurrences) return results;

        // Skip if a similar HIGH_AMOUNT or expression rule already exists
        if (HasExistingAmountRule(existing, threshold)) return results;

        var flagRate = totalCount > 0 ? (double)matchCount / totalCount * 100 : 0;
        var confidence = CalculateConfidence(matchCount, flagged.Count, flagRate);

        results.Add(new RuleSuggestion
        {
            Name = $"High Amount ≥ ${threshold:N0}",
            Description = $"{matchCount} flagged transactions had amounts ≥ ${threshold:N0} " +
                          $"({flagRate:F1}% flag rate in this range)",
            Category = "Amount",
            ExpressionField = "Amount",
            ExpressionOperator = ">=",
            ExpressionValue = threshold.ToString("F0"),
            SuggestedWeight = matchCount > 20 ? 25 : 15,
            Confidence = confidence,
            Pattern = SuggestionPattern.AmountCluster,
            Evidence = $"{matchCount} of {flagged.Count} flagged txns matched (${amounts.Min():N0}–${amounts.Max():N0} range)"
        });

        return results;
    }

    /// <summary>
    /// Detect source→destination country corridors with high flag rates.
    /// </summary>
    private List<RuleSuggestion> DetectCorridorHotspots(
        List<FlaggedTransaction> flagged, int totalCount, List<RiskRule> existing)
    {
        var results = new List<RuleSuggestion>();

        // Group flagged txns by source country
        var sourceGroups = flagged
            .Where(f => !string.IsNullOrEmpty(f.Transaction.SourceCountry))
            .GroupBy(f => f.Transaction.SourceCountry)
            .Where(g => g.Count() >= MinPatternOccurrences)
            .OrderByDescending(g => g.Count());

        foreach (var group in sourceGroups)
        {
            var country = group.Key;
            if (HasExistingFieldRule(existing, "SourceCountry", "==", country)) continue;

            var matchCount = group.Count();
            var flagRate = totalCount > 0 ? (double)matchCount / totalCount * 100 : 0;
            var confidence = CalculateConfidence(matchCount, flagged.Count, flagRate);

            results.Add(new RuleSuggestion
            {
                Name = $"High-Risk Source: {country}",
                Description = $"{matchCount} flagged transactions originated from {country}",
                Category = "Geography",
                ExpressionField = "SourceCountry",
                ExpressionOperator = "==",
                ExpressionValue = country,
                SuggestedWeight = matchCount > 15 ? 20 : 12,
                Confidence = confidence,
                Pattern = SuggestionPattern.CorridorHotspot,
                Evidence = $"{matchCount} of {flagged.Count} flagged txns from {country}"
            });
        }

        // Also check destination country
        var destGroups = flagged
            .Where(f => !string.IsNullOrEmpty(f.Transaction.DestinationCountry))
            .GroupBy(f => f.Transaction.DestinationCountry)
            .Where(g => g.Count() >= MinPatternOccurrences)
            .OrderByDescending(g => g.Count());

        foreach (var group in destGroups)
        {
            var country = group.Key;
            if (HasExistingFieldRule(existing, "DestinationCountry", "==", country)) continue;

            var matchCount = group.Count();
            var flagRate = totalCount > 0 ? (double)matchCount / totalCount * 100 : 0;
            var confidence = CalculateConfidence(matchCount, flagged.Count, flagRate);

            results.Add(new RuleSuggestion
            {
                Name = $"High-Risk Destination: {country}",
                Description = $"{matchCount} flagged transactions sent to {country}",
                Category = "Geography",
                ExpressionField = "DestinationCountry",
                ExpressionOperator = "==",
                ExpressionValue = country,
                SuggestedWeight = matchCount > 15 ? 20 : 12,
                Confidence = confidence,
                Pattern = SuggestionPattern.CorridorHotspot,
                Evidence = $"{matchCount} of {flagged.Count} flagged txns to {country}"
            });
        }

        return results;
    }

    /// <summary>
    /// Detect hours where flagged transactions cluster (e.g., 2-5 AM UTC).
    /// </summary>
    private List<RuleSuggestion> DetectTimeAnomalies(
        List<FlaggedTransaction> flagged, int totalCount, List<RiskRule> existing)
    {
        var results = new List<RuleSuggestion>();

        // Bucket by hour
        var hourCounts = flagged
            .GroupBy(f => f.Transaction.CreatedAt.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .OrderByDescending(h => h.Count)
            .ToList();

        if (hourCounts.Count == 0) return results;

        var avgPerHour = (double)flagged.Count / 24;

        // Find hours with significantly above-average flagged counts (>2x average)
        foreach (var hc in hourCounts.Where(h => h.Count > avgPerHour * 2 && h.Count >= MinPatternOccurrences))
        {
            var hourStr = hc.Hour.ToString();
            if (HasExistingFieldRule(existing, "TransactionHour", "==", hourStr)) continue;

            var flagRate = totalCount > 0 ? (double)hc.Count / totalCount * 100 : 0;
            var confidence = CalculateConfidence(hc.Count, flagged.Count, flagRate);

            results.Add(new RuleSuggestion
            {
                Name = $"Suspicious Hour: {hc.Hour:D2}:00 UTC",
                Description = $"{hc.Count} flagged transactions occurred at {hc.Hour:D2}:00 UTC " +
                              $"({hc.Count / avgPerHour:F1}x the hourly average)",
                Category = "Pattern",
                ExpressionField = "TransactionHour",
                ExpressionOperator = "==",
                ExpressionValue = hourStr,
                SuggestedWeight = 10,
                Confidence = confidence,
                Pattern = SuggestionPattern.TimeAnomaly,
                Evidence = $"{hc.Count} flagged txns at {hc.Hour:D2}:00 (avg {avgPerHour:F1}/hr)"
            });
        }

        return results;
    }

    /// <summary>
    /// Detect if flagged transactions have an unusual concentration of round amounts.
    /// </summary>
    private List<RuleSuggestion> DetectRoundAmountSpikes(
        List<FlaggedTransaction> flagged, int totalCount, List<RiskRule> existing)
    {
        var results = new List<RuleSuggestion>();

        // Round amounts divisible by 1000
        var roundAmountTxns = flagged
            .Where(f => f.Transaction.Amount >= 1000 && f.Transaction.Amount % 1000 == 0)
            .ToList();

        if (roundAmountTxns.Count < MinPatternOccurrences) return results;

        var roundPct = (double)roundAmountTxns.Count / flagged.Count * 100;

        // Only suggest if >30% of flagged txns are round amounts (unusual)
        if (roundPct < 30) return results;

        // Find the minimum round amount that catches most
        var minRound = roundAmountTxns.Select(f => f.Transaction.Amount).OrderBy(a => a).First();
        var threshold = Math.Max(1000, RoundToCleanThreshold(minRound));
        var thresholdStr = threshold.ToString("F0");

        if (HasExistingFieldRule(existing, "Amount", ">=", thresholdStr)) return results;

        var confidence = CalculateConfidence(roundAmountTxns.Count, flagged.Count, roundPct);

        results.Add(new RuleSuggestion
        {
            Name = $"Round Amounts ≥ ${threshold:N0}",
            Description = $"{roundAmountTxns.Count} flagged transactions had round amounts (÷1000) — " +
                          $"{roundPct:F0}% of all flagged activity",
            Category = "Pattern",
            ExpressionField = "Amount",
            ExpressionOperator = ">=",
            ExpressionValue = thresholdStr,
            SuggestedWeight = 12,
            Confidence = confidence,
            Pattern = SuggestionPattern.RoundAmountSpike,
            Evidence = $"{roundAmountTxns.Count} round-amount txns ({roundPct:F0}% of flagged)"
        });

        return results;
    }

    /// <summary>
    /// Detect specific currency pairs that appear frequently in flagged transactions.
    /// </summary>
    private List<RuleSuggestion> DetectCurrencyPairRisks(
        List<FlaggedTransaction> flagged, int totalCount, List<RiskRule> existing)
    {
        var results = new List<RuleSuggestion>();

        var currencyGroups = flagged
            .Where(f => !string.IsNullOrEmpty(f.Transaction.SourceCurrency))
            .GroupBy(f => f.Transaction.SourceCurrency)
            .Where(g => g.Count() >= MinPatternOccurrences)
            .OrderByDescending(g => g.Count());

        foreach (var group in currencyGroups)
        {
            var currency = group.Key;
            if (HasExistingFieldRule(existing, "SourceCurrency", "==", currency)) continue;

            var matchCount = group.Count();
            var flagRate = totalCount > 0 ? (double)matchCount / totalCount * 100 : 0;
            var confidence = CalculateConfidence(matchCount, flagged.Count, flagRate);

            // Only suggest if this currency appears in >20% of flagged txns
            if ((double)matchCount / flagged.Count < 0.2) continue;

            results.Add(new RuleSuggestion
            {
                Name = $"Risky Currency: {currency}",
                Description = $"{matchCount} flagged transactions used {currency} as source currency",
                Category = "Pattern",
                ExpressionField = "SourceCurrency",
                ExpressionOperator = "==",
                ExpressionValue = currency,
                SuggestedWeight = 10,
                Confidence = confidence,
                Pattern = SuggestionPattern.CurrencyPairRisk,
                Evidence = $"{matchCount} of {flagged.Count} flagged txns in {currency}"
            });
        }

        return results;
    }

    /// <summary>
    /// Detect customers sending many transactions in short windows.
    /// Suggests a TotalTransactions threshold rule.
    /// </summary>
    private async Task<List<RuleSuggestion>> DetectVelocityBurstsAsync(
        ApplicationDbContext db, string tenantId, DateTime cutoff, List<RiskRule> existing)
    {
        var results = new List<RuleSuggestion>();

        // Find customers with high txn counts in the period
        var velocityData = await db.Transactions
            .Where(t => t.TenantId == tenantId && t.CreatedAt >= cutoff)
            .GroupBy(t => t.SenderId)
            .Select(g => new { SenderId = g.Key, Count = g.Count() })
            .Where(g => g.Count >= 10) // At least 10 txns
            .OrderByDescending(g => g.Count)
            .Take(50)
            .ToListAsync();

        if (velocityData.Count < MinPatternOccurrences) return results;

        // Check how many of those high-velocity senders had flagged txns
        var highVelocitySenders = velocityData.Select(v => v.SenderId).ToHashSet();
        var flaggedHighVelocity = await db.Transactions
            .Where(t => t.TenantId == tenantId && t.CreatedAt >= cutoff &&
                        highVelocitySenders.Contains(t.SenderId))
            .Join(db.RiskAnalyses.Where(ra => ra.TenantId == tenantId && ra.RiskLevel >= RiskLevel.Medium),
                t => t.Id, ra => ra.TransactionId,
                (t, _) => t.SenderId)
            .Distinct()
            .CountAsync();

        if (flaggedHighVelocity < MinPatternOccurrences) return results;

        // Suggest a TotalTransactions threshold at the median velocity
        var medianVelocity = velocityData[velocityData.Count / 2].Count;
        var thresholdStr = medianVelocity.ToString();

        if (HasExistingFieldRule(existing, "TotalTransactions", ">=", thresholdStr)) return results;

        var confidence = CalculateConfidence(flaggedHighVelocity, velocityData.Count, 
            (double)flaggedHighVelocity / velocityData.Count * 100);

        results.Add(new RuleSuggestion
        {
            Name = $"High-Volume Customers ≥ {medianVelocity} txns",
            Description = $"{flaggedHighVelocity} high-volume customers (≥{medianVelocity} txns) " +
                          $"had flagged transactions in the last 30 days",
            Category = "Velocity",
            ExpressionField = "TotalTransactions",
            ExpressionOperator = ">=",
            ExpressionValue = thresholdStr,
            SuggestedWeight = 15,
            Confidence = confidence,
            Pattern = SuggestionPattern.VelocityBurst,
            Evidence = $"{flaggedHighVelocity} of {velocityData.Count} high-velocity senders were flagged"
        });

        return results;
    }

    /// <summary>
    /// Detect customers who have been flagged multiple times (repeat offenders).
    /// Suggests a FlaggedCount threshold rule.
    /// </summary>
    private async Task<List<RuleSuggestion>> DetectRepeatOffendersAsync(
        ApplicationDbContext db, string tenantId, DateTime cutoff, List<RiskRule> existing)
    {
        var results = new List<RuleSuggestion>();

        // Find senders with multiple flagged txns
        var repeatOffenders = await db.Transactions
            .Where(t => t.TenantId == tenantId && t.CreatedAt >= cutoff)
            .Join(db.RiskAnalyses.Where(ra => ra.TenantId == tenantId && ra.RiskLevel >= RiskLevel.Medium),
                t => t.Id, ra => ra.TransactionId,
                (t, _) => t.SenderId)
            .GroupBy(s => s)
            .Select(g => new { SenderId = g.Key, FlagCount = g.Count() })
            .Where(g => g.FlagCount >= 2)
            .OrderByDescending(g => g.FlagCount)
            .ToListAsync();

        if (repeatOffenders.Count < MinPatternOccurrences) return results;

        // Suggest a FlaggedCount threshold at the median
        var medianFlagCount = repeatOffenders[repeatOffenders.Count / 2].FlagCount;
        var threshold = Math.Max(2, medianFlagCount);
        var thresholdStr = threshold.ToString();

        if (HasExistingFieldRule(existing, "FlaggedCount", ">=", thresholdStr)) return results;

        var confidence = CalculateConfidence(repeatOffenders.Count, repeatOffenders.Count, 80);

        results.Add(new RuleSuggestion
        {
            Name = $"Repeat Offenders ≥ {threshold} flags",
            Description = $"{repeatOffenders.Count} customers have been flagged ≥{threshold} times — " +
                          $"top offender was flagged {repeatOffenders.First().FlagCount} times",
            Category = "Pattern",
            ExpressionField = "FlaggedCount",
            ExpressionOperator = ">=",
            ExpressionValue = thresholdStr,
            SuggestedWeight = 20,
            Confidence = confidence,
            Pattern = SuggestionPattern.RepeatOffender,
            Evidence = $"{repeatOffenders.Count} repeat offenders (max {repeatOffenders.First().FlagCount} flags)"
        });

        return results;
    }

    /// <summary>
    /// Detect if newly onboarded customers are disproportionately flagged.
    /// Suggests a TotalTransactions &lt; N rule for new customers.
    /// </summary>
    private async Task<List<RuleSuggestion>> DetectNewCustomerRiskAsync(
        ApplicationDbContext db, string tenantId, DateTime cutoff,
        List<FlaggedTransaction> flagged, List<RiskRule> existing)
    {
        var results = new List<RuleSuggestion>();

        // Get customer profiles with low txn counts
        var newCustomerIds = await db.CustomerProfiles
            .Where(cp => cp.TenantId == tenantId && cp.TotalTransactions <= 5)
            .Select(cp => cp.ExternalId)
            .ToListAsync();

        if (newCustomerIds.Count == 0) return results;

        var newCustomerSet = newCustomerIds.ToHashSet();
        var flaggedNewCustomers = flagged
            .Where(f => newCustomerSet.Contains(f.Transaction.SenderId))
            .ToList();

        if (flaggedNewCustomers.Count < MinPatternOccurrences) return results;

        var pctOfFlagged = (double)flaggedNewCustomers.Count / flagged.Count * 100;

        // Only suggest if new customers account for >25% of flagged txns
        if (pctOfFlagged < 25) return results;

        if (HasExistingFieldRule(existing, "TotalTransactions", "<=", "5")) return results;

        var confidence = CalculateConfidence(flaggedNewCustomers.Count, flagged.Count, pctOfFlagged);

        results.Add(new RuleSuggestion
        {
            Name = "New Customer Risk (≤5 txns)",
            Description = $"{flaggedNewCustomers.Count} flagged transactions came from new customers (≤5 total txns) — " +
                          $"{pctOfFlagged:F0}% of all flagged activity",
            Category = "Pattern",
            ExpressionField = "TotalTransactions",
            ExpressionOperator = "<=",
            ExpressionValue = "5",
            SuggestedWeight = 15,
            Confidence = confidence,
            Pattern = SuggestionPattern.NewCustomerRisk,
            Evidence = $"{flaggedNewCustomers.Count} new-customer txns flagged ({pctOfFlagged:F0}% of flagged)"
        });

        return results;
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Calculate confidence score (0-100) based on match count, proportion, and flag rate.
    /// </summary>
    private static int CalculateConfidence(int matchCount, int totalFlagged, double flagRate)
    {
        // Component 1: Sample size (more matches = higher confidence, caps at 40)
        var sampleScore = Math.Min(40, matchCount * 4);

        // Component 2: Proportion of flagged txns caught (caps at 35)
        var proportionScore = totalFlagged > 0
            ? Math.Min(35, (int)((double)matchCount / totalFlagged * 35))
            : 0;

        // Component 3: Flag rate bonus (higher rate = cleaner signal, caps at 25)
        var rateScore = Math.Min(25, (int)(flagRate * 0.5));

        return Math.Min(100, sampleScore + proportionScore + rateScore);
    }

    /// <summary>Round an amount to a clean threshold (nearest 500 or 1000).</summary>
    private static decimal RoundToCleanThreshold(decimal amount)
    {
        if (amount >= 10000) return Math.Floor(amount / 1000) * 1000;
        if (amount >= 1000) return Math.Floor(amount / 500) * 500;
        return Math.Floor(amount / 100) * 100;
    }

    /// <summary>Check if an existing rule already covers a similar amount threshold.</summary>
    private static bool HasExistingAmountRule(List<RiskRule> existing, decimal threshold)
    {
        return existing.Any(r =>
            r.Mode != "Disabled" &&
            ((r.RuleCode == "HIGH_AMOUNT" && Math.Abs(r.Threshold - threshold) < threshold * 0.2m) ||
             (r.ExpressionField == "Amount" && r.ExpressionOperator == ">=" &&
              decimal.TryParse(r.ExpressionValue, out var v) && Math.Abs(v - threshold) < threshold * 0.2m)));
    }

    /// <summary>Check if an existing expression rule already matches this field/operator/value.</summary>
    private static bool HasExistingFieldRule(List<RiskRule> existing, string field, string op, string value)
    {
        return existing.Any(r =>
            r.Mode != "Disabled" &&
            r.ExpressionField == field &&
            r.ExpressionOperator == op &&
            string.Equals(r.ExpressionValue, value, StringComparison.OrdinalIgnoreCase));
    }

    // ── Internal DTO ─────────────────────────────────────────────

    private class FlaggedTransaction
    {
        public Transaction Transaction { get; set; } = null!;
        public int RiskScore { get; set; }
        public RiskLevel RiskLevel { get; set; }
    }
}
