using Microsoft.EntityFrameworkCore;
using PayGuardAI.Core.Entities;

namespace PayGuardAI.Data.ML;

/// <summary>
/// Extracts ML features from a Transaction + CustomerProfile into a TransactionMLInput vector.
/// 26 engineered features covering amount, time, type, geography, customer maturity, and velocity.
/// </summary>
public static class MLFeatureExtractor
{
    // Countries with elevated AML/CFT risk (FATF high-risk jurisdictions)
    private static readonly HashSet<string> HighRiskCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        "IR", "KP", "SY", "YE", "VE", "CU", "MM", "AF"
    };

    /// <summary>
    /// Extract features for real-time prediction (requires DB for velocity).
    /// </summary>
    public static async Task<TransactionMLInput> ExtractFeaturesAsync(
        Transaction transaction,
        CustomerProfile profile,
        ApplicationDbContext context,
        CancellationToken cancellationToken = default)
    {
        // Compute velocity features from DB
        var cutoff24h = transaction.CreatedAt.AddHours(-24);
        var recentTxns = await context.Transactions
            .Where(t => t.SenderId == transaction.SenderId && t.CreatedAt >= cutoff24h && t.Id != transaction.Id)
            .Select(t => t.Amount)
            .ToListAsync(cancellationToken);

        int velocity24h = recentTxns.Count;
        decimal volume24h = recentTxns.Sum();

        return ExtractFeatures(transaction, profile, velocity24h, volume24h);
    }

    /// <summary>
    /// Extract features when velocity is pre-computed (for training batch).
    /// </summary>
    public static TransactionMLInput ExtractFeatures(
        Transaction transaction,
        CustomerProfile profile,
        int velocity24h,
        decimal volume24h,
        bool? label = null)
    {
        var type = transaction.Type.ToUpperInvariant();
        var hour = transaction.CreatedAt.Hour;
        var dayOfWeek = (int)transaction.CreatedAt.DayOfWeek;
        var amount = (float)transaction.Amount;

        // Customer age in days
        float customerAgeDays = profile.FirstTransactionAt.HasValue
            ? (float)(transaction.CreatedAt - profile.FirstTransactionAt.Value).TotalDays
            : 0f;

        // Amount deviation from customer's average (z-score-like)
        float amountDeviation = profile.AverageTransactionAmount > 0
            ? amount / (float)profile.AverageTransactionAmount
            : 1f;

        // Flag and reject rates
        float flagRate = profile.TotalTransactions > 0
            ? (float)profile.FlaggedTransactionCount / profile.TotalTransactions
            : 0f;
        float rejectRate = profile.TotalTransactions > 0
            ? (float)profile.RejectedTransactionCount / profile.TotalTransactions
            : 0f;

        var input = new TransactionMLInput
        {
            // Amount
            Amount = amount,
            AmountLog = MathF.Log(amount + 1f),
            IsRoundAmount = (amount >= 1000 && amount % 1000 == 0) ? 1f : 0f,

            // Temporal
            HourOfDay = hour,
            DayOfWeek = dayOfWeek,
            IsWeekend = (dayOfWeek == 0 || dayOfWeek == 6) ? 1f : 0f,
            IsNightTime = (hour >= 2 && hour <= 5) ? 1f : 0f,

            // Transaction type one-hot
            IsSend = type == "SEND" ? 1f : 0f,
            IsReceive = type == "RECEIVE" ? 1f : 0f,
            IsDeposit = type == "DEPOSIT" ? 1f : 0f,
            IsWithdraw = type == "WITHDRAW" ? 1f : 0f,

            // Geography
            IsCrossBorder = (!string.IsNullOrEmpty(transaction.SourceCountry) &&
                             !string.IsNullOrEmpty(transaction.DestinationCountry) &&
                             !transaction.SourceCountry.Equals(transaction.DestinationCountry, StringComparison.OrdinalIgnoreCase))
                            ? 1f : 0f,
            IsCrossCurrency = (!string.IsNullOrEmpty(transaction.SourceCurrency) &&
                               !string.IsNullOrEmpty(transaction.DestinationCurrency) &&
                               !transaction.SourceCurrency.Equals(transaction.DestinationCurrency, StringComparison.OrdinalIgnoreCase))
                              ? 1f : 0f,
            IsHighRiskCountry = (HighRiskCountries.Contains(transaction.SourceCountry) ||
                                 HighRiskCountries.Contains(transaction.DestinationCountry))
                                ? 1f : 0f,

            // Customer profile
            CustomerAgeDays = customerAgeDays,
            TotalTransactions = profile.TotalTransactions,
            TotalVolume = (float)profile.TotalVolume,
            AverageAmount = (float)profile.AverageTransactionAmount,
            MaxAmount = (float)profile.MaxTransactionAmount,
            AmountDeviation = amountDeviation,
            KycLevel = (float)profile.KycLevel,
            RiskTier = (float)profile.RiskTier,
            FlagRate = flagRate,
            RejectRate = rejectRate,

            // Velocity
            Velocity24h = velocity24h,
            Volume24h = (float)volume24h,

            // Label (set only for training)
            Label = label ?? false
        };

        return input;
    }

    /// <summary>
    /// Feature names for XAI — maps index to human-readable name.
    /// </summary>
    public static readonly string[] FeatureNames =
    [
        "Amount",
        "Amount (log)",
        "Round Amount",
        "Hour of Day",
        "Day of Week",
        "Weekend",
        "Night Time (2-5 AM)",
        "Type: Send",
        "Type: Receive",
        "Type: Deposit",
        "Type: Withdraw",
        "Cross-Border",
        "Cross-Currency",
        "High-Risk Country",
        "Customer Age (days)",
        "Total Transactions",
        "Total Volume",
        "Average Amount",
        "Max Amount",
        "Amount vs Average",
        "KYC Level",
        "Risk Tier",
        "Flag Rate",
        "Reject Rate",
        "24h Transaction Count",
        "24h Volume"
    ];

    /// <summary>
    /// Get the feature values as an array (for importance analysis).
    /// </summary>
    public static float[] ToArray(TransactionMLInput input) =>
    [
        input.Amount,
        input.AmountLog,
        input.IsRoundAmount,
        input.HourOfDay,
        input.DayOfWeek,
        input.IsWeekend,
        input.IsNightTime,
        input.IsSend,
        input.IsReceive,
        input.IsDeposit,
        input.IsWithdraw,
        input.IsCrossBorder,
        input.IsCrossCurrency,
        input.IsHighRiskCountry,
        input.CustomerAgeDays,
        input.TotalTransactions,
        input.TotalVolume,
        input.AverageAmount,
        input.MaxAmount,
        input.AmountDeviation,
        input.KycLevel,
        input.RiskTier,
        input.FlagRate,
        input.RejectRate,
        input.Velocity24h,
        input.Volume24h
    ];
}
