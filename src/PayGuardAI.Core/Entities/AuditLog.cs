namespace PayGuardAI.Core.Entities;

/// <summary>
/// Audit log entry for compliance and regulatory requirements.
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Tenant this audit log belongs to.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;
    
    /// <summary>
    /// Action performed (e.g., "TRANSACTION_ANALYZED", "REVIEW_APPROVED").
    /// </summary>
    public string Action { get; set; } = string.Empty;
    
    /// <summary>
    /// Entity type affected (e.g., "Transaction", "RiskRule").
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// ID of the affected entity.
    /// </summary>
    public string EntityId { get; set; } = string.Empty;
    
    /// <summary>
    /// User who performed the action.
    /// </summary>
    public string PerformedBy { get; set; } = "system";
    
    /// <summary>
    /// IP address of the user.
    /// </summary>
    public string? IpAddress { get; set; }
    
    /// <summary>
    /// Previous state (JSON) for change tracking.
    /// </summary>
    public string? OldValues { get; set; }
    
    /// <summary>
    /// New state (JSON) for change tracking.
    /// </summary>
    public string? NewValues { get; set; }
    
    /// <summary>
    /// Additional context or notes.
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// When the action occurred.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
