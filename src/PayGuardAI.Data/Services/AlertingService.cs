using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Default alerting implementation (logs alerts).
/// </summary>
public class AlertingService : IAlertingService
{
    private readonly ILogger<AlertingService> _logger;

    public AlertingService(ILogger<AlertingService> logger)
    {
        _logger = logger;
    }

    public Task AlertAsync(string message, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("ALERT: {Message}", message);
        return Task.CompletedTask;
    }
}
