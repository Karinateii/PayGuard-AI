using Microsoft.ML.Data;

namespace PayGuardAI.Data.ML;

/// <summary>
/// ML.NET input schema — 26 engineered features extracted from Transaction + CustomerProfile.
/// Each field maps to a column in the ML.NET data view.
/// </summary>
public class TransactionMLInput
{
    // ── Transaction amount features ─────────────────────────────────
    [LoadColumn(0)]
    public float Amount { get; set; }

    [LoadColumn(1)]
    public float AmountLog { get; set; }

    [LoadColumn(2)]
    public float IsRoundAmount { get; set; }

    // ── Temporal features ───────────────────────────────────────────
    [LoadColumn(3)]
    public float HourOfDay { get; set; }

    [LoadColumn(4)]
    public float DayOfWeek { get; set; }

    [LoadColumn(5)]
    public float IsWeekend { get; set; }

    [LoadColumn(6)]
    public float IsNightTime { get; set; }

    // ── Transaction type one-hot encoding ───────────────────────────
    [LoadColumn(7)]
    public float IsSend { get; set; }

    [LoadColumn(8)]
    public float IsReceive { get; set; }

    [LoadColumn(9)]
    public float IsDeposit { get; set; }

    [LoadColumn(10)]
    public float IsWithdraw { get; set; }

    // ── Geographic features ─────────────────────────────────────────
    [LoadColumn(11)]
    public float IsCrossBorder { get; set; }

    [LoadColumn(12)]
    public float IsCrossCurrency { get; set; }

    [LoadColumn(13)]
    public float IsHighRiskCountry { get; set; }

    // ── Customer profile features ───────────────────────────────────
    [LoadColumn(14)]
    public float CustomerAgeDays { get; set; }

    [LoadColumn(15)]
    public float TotalTransactions { get; set; }

    [LoadColumn(16)]
    public float TotalVolume { get; set; }

    [LoadColumn(17)]
    public float AverageAmount { get; set; }

    [LoadColumn(18)]
    public float MaxAmount { get; set; }

    [LoadColumn(19)]
    public float AmountDeviation { get; set; }

    [LoadColumn(20)]
    public float KycLevel { get; set; }

    [LoadColumn(21)]
    public float RiskTier { get; set; }

    [LoadColumn(22)]
    public float FlagRate { get; set; }

    [LoadColumn(23)]
    public float RejectRate { get; set; }

    // ── Velocity features ───────────────────────────────────────────
    [LoadColumn(24)]
    public float Velocity24h { get; set; }

    [LoadColumn(25)]
    public float Volume24h { get; set; }

    // ── Label ───────────────────────────────────────────────────────
    /// <summary>
    /// Training label: true = fraud (Rejected), false = legitimate (Approved).
    /// </summary>
    [LoadColumn(26)]
    public bool Label { get; set; }
}

/// <summary>
/// ML.NET prediction output schema for binary classification.
/// </summary>
public class TransactionMLOutput
{
    /// <summary>
    /// Predicted class: true = fraud, false = legitimate.
    /// </summary>
    [ColumnName("PredictedLabel")]
    public bool PredictedLabel { get; set; }

    /// <summary>
    /// Calibrated probability of fraud (0.0 – 1.0).
    /// </summary>
    [ColumnName("Probability")]
    public float Probability { get; set; }

    /// <summary>
    /// Raw model score (not calibrated).
    /// </summary>
    [ColumnName("Score")]
    public float Score { get; set; }
}
