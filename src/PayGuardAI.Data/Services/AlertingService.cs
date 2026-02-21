using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Fallback alerting implementation — logs alerts when Slack is disabled.
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

    public Task AlertTransactionAsync(
        string tenantId, string externalId, int riskScore,
        string riskLevel, decimal amount, string currency, string senderId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "ALERT [TRANSACTION]: Tenant={TenantId} ExternalId={ExternalId} Score={Score} Level={Level} Amount={Amount} {Currency} Sender={Sender}",
            tenantId, externalId, riskScore, riskLevel, amount, currency, senderId);
        return Task.CompletedTask;
    }
}
