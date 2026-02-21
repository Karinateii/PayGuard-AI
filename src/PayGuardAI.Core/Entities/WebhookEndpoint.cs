namespace PayGuardAI.Core.Entities;

/// <summary>
/// Webhook endpoint configuration for a tenant — where to send events, which events, retry policy.
/// </summary>
public class WebhookEndpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string[] Events { get; set; } = ["transaction.analyzed", "review.completed", "alert.triggered"];
    public bool IsActive { get; set; } = true;
    public string SigningSecret { get; set; } = Guid.NewGuid().ToString("N");
    public int MaxRetries { get; set; } = 3;
    public DateTime? LastDeliveryAt { get; set; }
    public string? LastDeliveryStatus { get; set; }    // "200 OK", "500 Error", "timeout"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
