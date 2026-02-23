using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PayGuardAI.Data.Services;

public interface IDatabaseMigrationService
{
    Task EnsureDatabaseReadyAsync();
    string GetActiveDatabaseType();
}

public class DatabaseMigrationService : IDatabaseMigrationService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseMigrationService> _logger;

    public DatabaseMigrationService(
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<DatabaseMigrationService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task EnsureDatabaseReadyAsync()
    {
        var dbType = GetActiveDatabaseType();
        _logger.LogInformation("Ensuring database is ready. Active database: {DatabaseType}", dbType);

        try
        {
            // EnsureCreated only works for NEW databases — it won't add columns to existing ones.
            // For existing databases, we run ALTER TABLE statements to add missing columns.
            await _context.Database.EnsureCreatedAsync();

            // Add missing TenantId columns to existing PostgreSQL/SQLite databases
            await AddMissingColumnsAsync(dbType);
            
            _logger.LogInformation("Database ready: {DatabaseType}", dbType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database: {DatabaseType}", dbType);
            throw;
        }
    }

    /// <summary>
    /// Adds TenantId (and other missing columns) to tables that predate the multi-tenancy migration.
    /// Safe to run repeatedly — only adds columns that don't already exist.
    /// </summary>
    private async Task AddMissingColumnsAsync(string dbType)
    {
        // Map of table name → list of (column, type, default)
        var columnMigrations = new (string Table, string Column, string DefaultValue)[]
        {
            ("Transactions",            "TenantId", "afriex-demo"),
            ("RiskAnalyses",            "TenantId", "afriex-demo"),
            ("RiskFactors",             "TenantId", "afriex-demo"),
            ("CustomerProfiles",        "TenantId", "afriex-demo"),
            ("RiskRules",               "TenantId", ""),
            ("AuditLogs",               "TenantId", "afriex-demo"),
            ("TenantSubscriptions",     "TenantId", "afriex-demo"),
            ("OrganizationSettings",    "TenantId", "afriex-demo"),
            ("TeamMembers",             "TenantId", "afriex-demo"),
            ("ApiKeys",                 "TenantId", "afriex-demo"),
            ("WebhookEndpoints",        "TenantId", "afriex-demo"),
            ("NotificationPreferences", "TenantId", "afriex-demo"),
            ("CustomRoles",             "TenantId", "afriex-demo"),
        };

        foreach (var (table, column, defaultValue) in columnMigrations)
        {
            try
            {
                if (dbType == "PostgreSQL")
                {
                    // PostgreSQL: check information_schema, then ALTER TABLE if missing
                    var checkSql = $"""
                        SELECT COUNT(*) FROM information_schema.columns
                        WHERE table_name = '{table}' AND column_name = '{column}'
                        """;

                    var exists = false;
                    await using (var cmd = _context.Database.GetDbConnection().CreateCommand())
                    {
                        await _context.Database.OpenConnectionAsync();
                        cmd.CommandText = checkSql;
                        var result = await cmd.ExecuteScalarAsync();
                        exists = Convert.ToInt64(result) > 0;
                    }

                    if (!exists)
                    {
                        var alterSql = $"""
                            ALTER TABLE "{table}" ADD COLUMN "{column}" TEXT NOT NULL DEFAULT '{defaultValue}'
                            """;
                        await _context.Database.ExecuteSqlRawAsync(alterSql);
                        _logger.LogInformation("Added missing column {Column} to {Table}", column, table);
                    }
                }
                else
                {
                    // SQLite: use pragma to check, then ALTER TABLE if missing
                    var hasTenantId = false;
                    await using (var cmd = _context.Database.GetDbConnection().CreateCommand())
                    {
                        await _context.Database.OpenConnectionAsync();
                        cmd.CommandText = $"PRAGMA table_info({table})";
                        await using var reader = await cmd.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
                            {
                                hasTenantId = true;
                                break;
                            }
                        }
                    }

                    if (!hasTenantId)
                    {
                        var alterSql = $"ALTER TABLE {table} ADD COLUMN {column} TEXT NOT NULL DEFAULT '{defaultValue}'";
                        await _context.Database.ExecuteSqlRawAsync(alterSql);
                        _logger.LogInformation("Added missing column {Column} to {Table}", column, table);
                    }
                }
            }
            catch (Exception ex)
            {
                // Table might not exist yet (e.g., new tables like NotificationPreferences)
                // — that's fine, EnsureCreated will handle them
                _logger.LogDebug(ex, "Skipping column migration for {Table}.{Column} (table may not exist yet)", table, column);
            }
        }
    }

    public string GetActiveDatabaseType()
    {
        var featureFlagsSection = _configuration.GetSection("FeatureFlags");
        var usePostgresStr = featureFlagsSection["PostgresEnabled"];
        var usePostgres = bool.TryParse(usePostgresStr, out var result) && result;
        return usePostgres ? "PostgreSQL" : "SQLite";
    }
}
