namespace PayGuardAI.Core.Services;

/// <summary>
/// Alerting service for critical events.
/// </summary>
public interface IAlertingService
{
    /// <summary>
    /// Emit an alert for a critical transaction or system event.
    /// </summary>
    Task AlertAsync(string message, CancellationToken cancellationToken = default);
}
