namespace PayGuardAI.Core.Entities;

/// <summary>
/// Persistent structured log entry stored in the database.
/// Warning-level and above logs are captured here for searchable log history,
/// incident response, and compliance audit requirements.
/// </summary>
public class SystemLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Serilog level: Warning, Error, Fatal</summary>
    public string Level { get; set; } = "";

    /// <summary>Rendered log message</summary>
    public string Message { get; set; } = "";

    /// <summary>Full exception text (if any)</summary>
    public string? Exception { get; set; }

    /// <summary>Source context / logger name (e.g. "PayGuardAI.Web.Services.RiskScoringService")</summary>
    public string? SourceContext { get; set; }

    /// <summary>Request correlation ID for cross-log tracing</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Tenant that was active during the log event</summary>
    public string? TenantId { get; set; }

    /// <summary>User who triggered the action (email or "anonymous")</summary>
    public string? UserId { get; set; }

    /// <summary>Request path (e.g. "/api/v1/transactions/analyze")</summary>
    public string? RequestPath { get; set; }

    /// <summary>Machine / environment name</summary>
    public string? MachineName { get; set; }

    /// <summary>Serialized JSON of all structured properties</summary>
    public string? Properties { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
