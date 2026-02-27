using PayGuardAI.Core.Entities;

namespace PayGuardAI.Core.Services;

/// <summary>
/// Calculates fraud savings — the dollar value of transactions that
/// PayGuard AI blocked or flagged-and-rejected. Powers the "Fraud Saved"
/// dashboard widget that justifies subscription ROI.
/// </summary>
public interface IFraudSavingsService
{
    /// <summary>
    /// Get fraud savings summary for the current tenant.
    /// </summary>
    Task<FraudSavingsSummary> GetSavingsSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get savings broken down by rule (which rules saved the most money).
    /// </summary>
    Task<List<RuleSavingsInfo>> GetSavingsByRuleAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Fraud savings summary DTO for dashboard display.
/// </summary>
public class FraudSavingsSummary
{
    /// <summary>Total amount saved this month (rejected/blocked transactions).</summary>
    public decimal SavedThisMonth { get; set; }

    /// <summary>Total amount saved last month (for trend comparison).</summary>
    public decimal SavedLastMonth { get; set; }

    /// <summary>Total amount saved this week.</summary>
    public decimal SavedThisWeek { get; set; }

    /// <summary>All-time total amount saved.</summary>
    public decimal SavedAllTime { get; set; }

    /// <summary>Number of transactions blocked/rejected this month.</summary>
    public int BlockedCountThisMonth { get; set; }

    /// <summary>Number of transactions blocked/rejected all time.</summary>
    public int BlockedCountAllTime { get; set; }

    /// <summary>
    /// Trend percentage vs. last month.
    /// Positive = saving more, negative = saving less.
    /// </summary>
    public double TrendPercent { get; set; }

    /// <summary>Primary currency code for display (most common source currency).</summary>
    public string CurrencyCode { get; set; } = "USD";
}

/// <summary>
/// Savings attributed to a specific rule.
/// </summary>
public class RuleSavingsInfo
{
    public string RuleName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal AmountSaved { get; set; }
    public int TransactionCount { get; set; }
}
