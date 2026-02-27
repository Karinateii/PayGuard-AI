using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PayGuardAI.Core.Entities;
using PayGuardAI.Data;
using Serilog.Core;
using Serilog.Events;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Custom Serilog sink that writes Warning-level and above log events to the
/// SystemLogs database table. This enables in-app log search, correlation
/// tracing, and 30-day retention for compliance.
///
/// Uses a bounded queue + background flush to avoid blocking the logging pipeline.
/// Falls back silently on DB errors (never crashes the app for a log write failure).
/// </summary>
public class DatabaseLogSink : ILogEventSink, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly System.Collections.Concurrent.ConcurrentQueue<SystemLog> _queue = new();
    private readonly Timer _flushTimer;
    private readonly int _batchSize;
    private bool _disposed;

    public DatabaseLogSink(IServiceScopeFactory scopeFactory, int flushIntervalSeconds = 5, int batchSize = 50)
    {
        _scopeFactory = scopeFactory;
        _batchSize = batchSize;
        _flushTimer = new Timer(_ => FlushAsync().ConfigureAwait(false), null,
            TimeSpan.FromSeconds(flushIntervalSeconds),
            TimeSpan.FromSeconds(flushIntervalSeconds));
    }

    // Sources that produce noisy infra warnings — skip persisting them to DB
    private static readonly HashSet<string> _suppressedSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.AspNetCore.Session.SessionMiddleware",
        "Microsoft.AspNetCore.DataProtection.XmlEncryption.XmlKeyManager",
        "Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository",
        "Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager",
        "Microsoft.EntityFrameworkCore.Model.Validation",
        "Microsoft.AspNetCore.Hosting.Diagnostics",
        "Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServer",
    };

    public void Emit(LogEvent logEvent)
    {
        if (_disposed) return;

        // Only persist Warning and above to keep DB lean
        if (logEvent.Level < LogEventLevel.Warning) return;

        // Skip noisy infrastructure sources
        var source = GetProperty(logEvent, "SourceContext");
        if (source != null && _suppressedSources.Any(s => source.Contains(s, StringComparison.OrdinalIgnoreCase)))
            return;

        var entry = new SystemLog
        {
            Level = logEvent.Level.ToString(),
            Message = logEvent.RenderMessage(),
            Exception = logEvent.Exception?.ToString(),
            SourceContext = GetProperty(logEvent, "SourceContext"),
            CorrelationId = GetProperty(logEvent, "CorrelationId"),
            TenantId = GetProperty(logEvent, "TenantId"),
            UserId = GetProperty(logEvent, "UserId"),
            RequestPath = GetProperty(logEvent, "RequestPath"),
            MachineName = GetProperty(logEvent, "EnvironmentName")
                          ?? Environment.MachineName,
            Properties = SerializeProperties(logEvent),
            CreatedAt = logEvent.Timestamp.UtcDateTime
        };

        _queue.Enqueue(entry);

        // If queue is getting large, trigger an immediate flush
        if (_queue.Count >= _batchSize * 2)
        {
            _ = FlushAsync();
        }
    }

    private async Task FlushAsync()
    {
        if (_queue.IsEmpty) return;

        var batch = new List<SystemLog>();
        while (batch.Count < _batchSize && _queue.TryDequeue(out var entry))
        {
            batch.Add(entry);
        }

        if (batch.Count == 0) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.SystemLogs.AddRange(batch);
            await db.SaveChangesAsync();
        }
        catch
        {
            // Never let a DB failure crash the logging pipeline.
            // Logs are best-effort persistent storage — console/Railway still has them.
        }
    }

    private static string? GetProperty(LogEvent logEvent, string name)
    {
        if (logEvent.Properties.TryGetValue(name, out var value))
        {
            // Strip quotes from scalar values
            var rendered = value.ToString();
            return rendered.Trim('"');
        }
        return null;
    }

    private static string? SerializeProperties(LogEvent logEvent)
    {
        if (logEvent.Properties.Count == 0) return null;

        var dict = new Dictionary<string, string>();
        foreach (var kvp in logEvent.Properties)
        {
            dict[kvp.Key] = kvp.Value.ToString().Trim('"');
        }

        try
        {
            return JsonSerializer.Serialize(dict);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _flushTimer.Dispose();

        // Final flush
        FlushAsync().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Extension method to add the DatabaseLogSink to Serilog configuration.
/// </summary>
public static class DatabaseLogSinkExtensions
{
    public static Serilog.LoggerConfiguration DatabaseSink(
        this Serilog.Configuration.LoggerSinkConfiguration sinkConfig,
        IServiceScopeFactory scopeFactory,
        int flushIntervalSeconds = 5,
        int batchSize = 50)
    {
        return sinkConfig.Sink(new DatabaseLogSink(scopeFactory, flushIntervalSeconds, batchSize));
    }
}
