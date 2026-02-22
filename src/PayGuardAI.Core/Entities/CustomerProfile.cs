namespace PayGuardAI.Core.Entities;

/// <summary>
/// Customer profile for risk assessment.
/// Built from transaction history to identify patterns.
/// </summary>
public class CustomerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Tenant this customer profile belongs to.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;
    
    /// <summary>
    /// External customer ID from Afriex.
    /// </summary>
    public string ExternalId { get; set; } = string.Empty;
    
    /// <summary>
    /// Total number of transactions processed.
    /// </summary>
    public int TotalTransactions { get; set; }
    
    /// <summary>
    /// Total volume (in USD equivalent) transacted.
    /// </summary>
    public decimal TotalVolume { get; set; }
    
    /// <summary>
    /// Average transaction amount.
    /// </summary>
    public decimal AverageTransactionAmount { get; set; }
    
    /// <summary>
    /// Maximum single transaction amount seen.
    /// </summary>
    public decimal MaxTransactionAmount { get; set; }
    
    /// <summary>
    /// Countries this customer typically transacts with.
    /// </summary>
    public string FrequentCountries { get; set; } = string.Empty;
    
    /// <summary>
    /// Customer's verified KYC level.
    /// </summary>
    public KycLevel KycLevel { get; set; } = KycLevel.Unknown;
    
    /// <summary>
    /// Overall risk tier based on history.
    /// </summary>
    public CustomerRiskTier RiskTier { get; set; } = CustomerRiskTier.Unknown;
    
    /// <summary>
    /// Number of flagged transactions.
    /// </summary>
    public int FlaggedTransactionCount { get; set; }
    
    /// <summary>
    /// Number of rejected transactions (by compliance).
    /// </summary>
    public int RejectedTransactionCount { get; set; }
    
    /// <summary>
    /// First transaction date.
    /// </summary>
    public DateTime? FirstTransactionAt { get; set; }
    
    /// <summary>
    /// Most recent transaction date.
    /// </summary>
    public DateTime? LastTransactionAt { get; set; }
    
    /// <summary>
    /// When this profile was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// KYC verification level.
/// </summary>
public enum KycLevel
{
    Unknown = 0,
    Tier1 = 1,  // Basic - ID verified
    Tier2 = 2,  // Enhanced - Address + ID
    Tier3 = 3   // Full - Complete verification
}

/// <summary>
/// Customer risk tier based on history.
/// </summary>
public enum CustomerRiskTier
{
    Unknown = 0,
    Trusted = 1,    // Good history, low risk
    Standard = 2,   // Normal monitoring
    Elevated = 3,   // Increased scrutiny
    HighRisk = 4    // Maximum monitoring
}
