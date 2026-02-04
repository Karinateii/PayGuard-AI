namespace PayGuardAI.Core.Entities;

/// <summary>
/// Represents a financial transaction received from Afriex webhooks.
/// </summary>
public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// External transaction ID from Afriex.
    /// </summary>
    public string ExternalId { get; set; } = string.Empty;
    
    /// <summary>
    /// Transaction type: SEND, RECEIVE, DEPOSIT, WITHDRAW, etc.
    /// </summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// Transaction status from Afriex.
    /// </summary>
    public string Status { get; set; } = string.Empty;
    
    /// <summary>
    /// Amount in the source currency.
    /// </summary>
    public decimal Amount { get; set; }
    
    /// <summary>
    /// Source currency code (e.g., NGN, USD, KES).
    /// </summary>
    public string SourceCurrency { get; set; } = string.Empty;
    
    /// <summary>
    /// Destination currency code.
    /// </summary>
    public string DestinationCurrency { get; set; } = string.Empty;
    
    /// <summary>
    /// Sender's customer ID.
    /// </summary>
    public string SenderId { get; set; } = string.Empty;
    
    /// <summary>
    /// Receiver's customer ID (for transfers).
    /// </summary>
    public string? ReceiverId { get; set; }
    
    /// <summary>
    /// Source country code.
    /// </summary>
    public string SourceCountry { get; set; } = string.Empty;
    
    /// <summary>
    /// Destination country code.
    /// </summary>
    public string DestinationCountry { get; set; } = string.Empty;
    
    /// <summary>
    /// When the transaction was created in Afriex.
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// When we received and processed the transaction.
    /// </summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Raw webhook payload for audit purposes.
    /// </summary>
    public string RawPayload { get; set; } = string.Empty;
    
    // Navigation properties
    public RiskAnalysis? RiskAnalysis { get; set; }
}
