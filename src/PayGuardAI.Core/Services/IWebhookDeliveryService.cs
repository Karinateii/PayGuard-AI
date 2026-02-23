namespace PayGuardAI.Core.Services;

/// <summary>
/// Delivers outbound webhook events to customer-configured endpoints.
/// Sends HMAC-SHA256-signed HTTP POST requests with retry logic.
/// </summary>
public interface IWebhookDeliveryService
{
    /// <summary>
    /// Deliver an event to all active webhook endpoints for the given tenant
    /// that are subscribed to the specified event type.
    /// </summary>
    /// <param name="tenantId">The tenant whose endpoints should receive the event.</param>
    /// <param name="eventType">Event type, e.g. "transaction.analyzed", "review.completed".</param>
    /// <param name="payload">The JSON-serializable event payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeliverEventAsync(string tenantId, string eventType, object payload, CancellationToken cancellationToken = default);
}
