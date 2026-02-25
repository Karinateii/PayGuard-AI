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

            // Create MagicLinkTokens table if it doesn't exist (added after initial DB)
            await CreateMagicLinkTokensTableAsync(dbType);

            // Create CustomReports table if it doesn't exist (added for Advanced Analytics)
            await CreateCustomReportsTableAsync(dbType);

            // Create MLModels table if it doesn't exist (added for ML Risk Scoring)
            await CreateMLModelsTableAsync(dbType);

            // Mark existing tenants as onboarded so they aren't redirected
            await MarkExistingTenantsOnboardedAsync(dbType);

            // Ensure the platform owner has a TeamMember record for auth lookup
            await EnsurePlatformOwnerTeamMemberAsync();
            
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
            // ── TenantId columns (multi-tenancy) ──
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

            // ── OrganizationSettings extras ──
            ("OrganizationSettings",    "IpWhitelist", ""),
            ("OrganizationSettings",    "UpdatedBy", "system"),
            ("OrganizationSettings",    "SlackWebhookUrl", ""),

            // ── TeamMember extras ──
            ("TeamMembers",             "DisplayName", ""),
            ("TeamMembers",             "Status", "active"),

            // ── TenantSubscription extras ──
            ("TenantSubscriptions",     "PaystackPlanCode", ""),
            ("TenantSubscriptions",     "PaystackSubscriptionCode", ""),
            ("TenantSubscriptions",     "BillingEmail", ""),
            ("TenantSubscriptions",     "Provider", "paystack"),

            // ── NotificationPreference extras ──
            ("NotificationPreferences", "DisplayName", ""),

            // ── CustomRole extras ──
            // (IsBuiltIn is boolean — handled separately below)
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

        // ── Fix unique indexes: must be per-tenant composites ──
        await FixRuleCodeUniqueIndexAsync(dbType);
        await FixExternalIdUniqueIndexesAsync(dbType);

        // ── Boolean columns need the correct native type (not TEXT) ──
        await AddBooleanColumnIfMissing(dbType, "OrganizationSettings", "OnboardingCompleted");
        await AddBooleanColumnIfMissing(dbType, "CustomRoles", "IsBuiltIn");
    }

    /// <summary>
    /// Adds a boolean column with correct native type: BOOLEAN (PostgreSQL) or INTEGER (SQLite).
    /// Defaults to false/0 for existing rows.
    /// </summary>
    private async Task AddBooleanColumnIfMissing(string dbType, string table, string column)
    {
        try
        {
            bool exists = false;

            if (dbType == "PostgreSQL")
            {
                var checkSql = $"SELECT COUNT(*) FROM information_schema.columns WHERE table_name = '{table}' AND column_name = '{column}'";
                await using var cmd = _context.Database.GetDbConnection().CreateCommand();
                await _context.Database.OpenConnectionAsync();
                cmd.CommandText = checkSql;
                exists = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;

                if (!exists)
                {
                    await _context.Database.ExecuteSqlRawAsync(
                        $"""ALTER TABLE "{table}" ADD COLUMN "{column}" BOOLEAN NOT NULL DEFAULT false""");
                    _logger.LogInformation("Added boolean column {Column} to {Table}", column, table);
                }
                else
                {
                    // Column might exist as wrong type (TEXT) from a previous migration — fix it
                    var typeSql = $"SELECT data_type FROM information_schema.columns WHERE table_name = '{table}' AND column_name = '{column}'";
                    cmd.CommandText = typeSql;
                    var dataType = (await cmd.ExecuteScalarAsync())?.ToString();
                    if (dataType == "text")
                    {
                        // Drop and re-add with correct type
                        var dropSql = $"ALTER TABLE \"{table}\" DROP COLUMN \"{column}\"";
                        var addSql = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" BOOLEAN NOT NULL DEFAULT false";
                        await _context.Database.ExecuteSqlRawAsync(dropSql);
                        await _context.Database.ExecuteSqlRawAsync(addSql);
                        _logger.LogInformation("Fixed column type for {Column} in {Table}: TEXT → BOOLEAN", column, table);
                    }
                }
            }
            else
            {
                // SQLite
                await using var cmd = _context.Database.GetDbConnection().CreateCommand();
                await _context.Database.OpenConnectionAsync();
                cmd.CommandText = $"PRAGMA table_info({table})";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    await _context.Database.ExecuteSqlRawAsync(
                        $"ALTER TABLE {table} ADD COLUMN {column} INTEGER NOT NULL DEFAULT 0");
                    _logger.LogInformation("Added boolean column {Column} to {Table}", column, table);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping boolean column migration for {Table}.{Column}", table, column);
        }
    }

    /// <summary>
    /// Creates the MagicLinkTokens table if it doesn't already exist.
    /// This table was added after the initial database creation.
    /// </summary>
    private async Task CreateMagicLinkTokensTableAsync(string dbType)
    {
        try
        {
            if (dbType == "PostgreSQL")
            {
                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE IF NOT EXISTS "MagicLinkTokens" (
                        "Id" uuid NOT NULL PRIMARY KEY,
                        "TokenHash" text NOT NULL,
                        "Email" text NOT NULL,
                        "ExpiresAt" timestamp with time zone NOT NULL,
                        "IsUsed" boolean NOT NULL DEFAULT false,
                        "RequestedFromIp" text NOT NULL DEFAULT '',
                        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now()
                    )
                    """);

                // Add indexes if missing (safe to run repeatedly)
                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_MagicLinkTokens_TokenHash"
                    ON "MagicLinkTokens" ("TokenHash")
                    """);
                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE INDEX IF NOT EXISTS "IX_MagicLinkTokens_Email"
                    ON "MagicLinkTokens" ("Email")
                    """);
            }
            else
            {
                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE IF NOT EXISTS MagicLinkTokens (
                        Id TEXT NOT NULL PRIMARY KEY,
                        TokenHash TEXT NOT NULL,
                        Email TEXT NOT NULL,
                        ExpiresAt TEXT NOT NULL,
                        IsUsed INTEGER NOT NULL DEFAULT 0,
                        RequestedFromIp TEXT NOT NULL DEFAULT '',
                        CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
                    )
                    """);

                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_MagicLinkTokens_TokenHash
                    ON MagicLinkTokens (TokenHash)
                    """);
                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE INDEX IF NOT EXISTS IX_MagicLinkTokens_Email
                    ON MagicLinkTokens (Email)
                    """);
            }

            _logger.LogDebug("MagicLinkTokens table ensured");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping MagicLinkTokens table creation");
        }
    }

    /// <summary>
    /// Creates the CustomReports table if it doesn't already exist.
    /// Added for the Advanced Analytics feature — stores user-defined report definitions.
    /// </summary>
    private async Task CreateCustomReportsTableAsync(string dbType)
    {
        try
        {
            if (dbType == "PostgreSQL")
            {
                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE IF NOT EXISTS "CustomReports" (
                        "Id" uuid NOT NULL PRIMARY KEY,
                        "TenantId" text NOT NULL DEFAULT '',
                        "Name" text NOT NULL DEFAULT '',
                        "Description" text,
                        "ReportType" text NOT NULL DEFAULT 'transactions',
                        "StartDate" timestamp with time zone,
                        "EndDate" timestamp with time zone,
                        "Filters" text,
                        "Grouping" text,
                        "IsScheduled" boolean NOT NULL DEFAULT false,
                        "ScheduleCron" text,
                        "EmailRecipients" text,
                        "CreatedBy" text NOT NULL DEFAULT '',
                        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now()
                    )
                    """);

                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE INDEX IF NOT EXISTS "IX_CustomReports_TenantId"
                    ON "CustomReports" ("TenantId")
                    """);
                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE INDEX IF NOT EXISTS "IX_CustomReports_TenantId_CreatedBy"
                    ON "CustomReports" ("TenantId", "CreatedBy")
                    """);
            }
            else
            {
                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE IF NOT EXISTS CustomReports (
                        Id TEXT NOT NULL PRIMARY KEY,
                        TenantId TEXT NOT NULL DEFAULT '',
                        Name TEXT NOT NULL DEFAULT '',
                        Description TEXT,
                        ReportType TEXT NOT NULL DEFAULT 'transactions',
                        StartDate TEXT,
                        EndDate TEXT,
                        Filters TEXT,
                        Grouping TEXT,
                        IsScheduled INTEGER NOT NULL DEFAULT 0,
                        ScheduleCron TEXT,
                        EmailRecipients TEXT,
                        CreatedBy TEXT NOT NULL DEFAULT '',
                        CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
                    )
                    """);

                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE INDEX IF NOT EXISTS IX_CustomReports_TenantId
                    ON CustomReports (TenantId)
                    """);
                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE INDEX IF NOT EXISTS IX_CustomReports_TenantId_CreatedBy
                    ON CustomReports (TenantId, CreatedBy)
                    """);
            }

            _logger.LogDebug("CustomReports table ensured");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping CustomReports table creation");
        }
    }

    /// <summary>
    /// Creates the MLModels table if it doesn't already exist.
    /// Added for the ML Risk Scoring feature — stores trained model binaries and metrics.
    /// </summary>
    private async Task CreateMLModelsTableAsync(string dbType)
    {
        try
        {
            if (dbType == "PostgreSQL")
            {
                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE IF NOT EXISTS "MLModels" (
                        "Id" uuid NOT NULL PRIMARY KEY,
                        "TenantId" text NOT NULL DEFAULT '',
                        "Version" text NOT NULL DEFAULT '',
                        "TrainerName" text NOT NULL DEFAULT 'FastTree',
                        "TrainingSamples" integer NOT NULL DEFAULT 0,
                        "FraudSamples" integer NOT NULL DEFAULT 0,
                        "LegitSamples" integer NOT NULL DEFAULT 0,
                        "Accuracy" double precision NOT NULL DEFAULT 0,
                        "AUC" double precision NOT NULL DEFAULT 0,
                        "F1Score" double precision NOT NULL DEFAULT 0,
                        "PositivePrecision" double precision NOT NULL DEFAULT 0,
                        "PositiveRecall" double precision NOT NULL DEFAULT 0,
                        "IsActive" boolean NOT NULL DEFAULT false,
                        "ModelData" bytea,
                        "TrainedAt" timestamp with time zone NOT NULL DEFAULT now(),
                        "TrainedBy" text NOT NULL DEFAULT 'system',
                        "Notes" text
                    )
                    """);

                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE INDEX IF NOT EXISTS "IX_MLModels_TenantId_IsActive"
                    ON "MLModels" ("TenantId", "IsActive")
                    """);
                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE INDEX IF NOT EXISTS "IX_MLModels_TrainedAt"
                    ON "MLModels" ("TrainedAt")
                    """);
            }
            else
            {
                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE IF NOT EXISTS MLModels (
                        Id TEXT NOT NULL PRIMARY KEY,
                        TenantId TEXT NOT NULL DEFAULT '',
                        Version TEXT NOT NULL DEFAULT '',
                        TrainerName TEXT NOT NULL DEFAULT 'FastTree',
                        TrainingSamples INTEGER NOT NULL DEFAULT 0,
                        FraudSamples INTEGER NOT NULL DEFAULT 0,
                        LegitSamples INTEGER NOT NULL DEFAULT 0,
                        Accuracy REAL NOT NULL DEFAULT 0,
                        AUC REAL NOT NULL DEFAULT 0,
                        F1Score REAL NOT NULL DEFAULT 0,
                        PositivePrecision REAL NOT NULL DEFAULT 0,
                        PositiveRecall REAL NOT NULL DEFAULT 0,
                        IsActive INTEGER NOT NULL DEFAULT 0,
                        ModelData BLOB,
                        TrainedAt TEXT NOT NULL DEFAULT (datetime('now')),
                        TrainedBy TEXT NOT NULL DEFAULT 'system',
                        Notes TEXT
                    )
                    """);

                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE INDEX IF NOT EXISTS IX_MLModels_TenantId_IsActive
                    ON MLModels (TenantId, IsActive)
                    """);
                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE INDEX IF NOT EXISTS IX_MLModels_TrainedAt
                    ON MLModels (TrainedAt)
                    """);
            }

            _logger.LogDebug("MLModels table ensured");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping MLModels table creation");
        }
    }

    /// <summary>
    /// <summary>
    /// Replaces globally-unique ExternalId indexes on Transactions and CustomerProfiles
    /// with per-tenant composite indexes (TenantId + ExternalId). Without this, a new
    /// tenant can't create a transaction or customer profile if the ExternalId already
    /// exists in another tenant's data.
    /// </summary>
    private async Task FixExternalIdUniqueIndexesAsync(string dbType)
    {
        var fixes = new[]
        {
            ("Transactions", "IX_Transactions_ExternalId", "IX_Transactions_TenantId_ExternalId"),
            ("CustomerProfiles", "IX_CustomerProfiles_ExternalId", "IX_CustomerProfiles_TenantId_ExternalId"),
        };

        foreach (var (table, oldIndex, newIndex) in fixes)
        {
            try
            {
                if (dbType == "PostgreSQL")
                {
                    var checkSql = $"SELECT COUNT(*) FROM pg_indexes WHERE tablename = '{table}' AND indexname = '{oldIndex}'";
                    await using var cmd = _context.Database.GetDbConnection().CreateCommand();
                    await _context.Database.OpenConnectionAsync();
                    cmd.CommandText = checkSql;
                    var exists = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;

                    if (exists)
                    {
                        await _context.Database.ExecuteSqlRawAsync($"DROP INDEX \"{oldIndex}\"");
                        await _context.Database.ExecuteSqlRawAsync(
                            $"CREATE UNIQUE INDEX \"{newIndex}\" ON \"{table}\" (\"TenantId\", \"ExternalId\")");
                        _logger.LogInformation("Fixed {Table} index: replaced {Old} with {New}", table, oldIndex, newIndex);
                    }
                }
                else
                {
                    await using var cmd = _context.Database.GetDbConnection().CreateCommand();
                    await _context.Database.OpenConnectionAsync();
                    cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='{oldIndex}'";
                    var exists = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;

                    if (exists)
                    {
                        await _context.Database.ExecuteSqlRawAsync($"DROP INDEX IF EXISTS {oldIndex}");
                        await _context.Database.ExecuteSqlRawAsync(
                            $"CREATE UNIQUE INDEX {newIndex} ON {table} (TenantId, ExternalId)");
                        _logger.LogInformation("Fixed {Table} index: replaced {Old} with {New}", table, oldIndex, newIndex);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipping ExternalId index fix for {Table} (table may not exist yet)", table);
            }
        }
    }

    /// <summary>
    /// Drops the old global IX_RiskRules_RuleCode unique index and replaces it with
    /// a composite IX_RiskRules_TenantId_RuleCode index so each tenant can have rules
    /// with the same RuleCode.
    /// </summary>
    private async Task FixRuleCodeUniqueIndexAsync(string dbType)
    {
        try
        {
            if (dbType == "PostgreSQL")
            {
                // Check if the old single-column unique index exists
                var checkSql = "SELECT COUNT(*) FROM pg_indexes WHERE tablename = 'RiskRules' AND indexname = 'IX_RiskRules_RuleCode'";
                await using var cmd = _context.Database.GetDbConnection().CreateCommand();
                await _context.Database.OpenConnectionAsync();
                cmd.CommandText = checkSql;
                var exists = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;

                if (exists)
                {
                    await _context.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_RiskRules_RuleCode\"");
                    await _context.Database.ExecuteSqlRawAsync(
                        "CREATE UNIQUE INDEX \"IX_RiskRules_TenantId_RuleCode\" ON \"RiskRules\" (\"TenantId\", \"RuleCode\")");
                    _logger.LogInformation("Fixed RiskRules index: replaced IX_RiskRules_RuleCode with IX_RiskRules_TenantId_RuleCode");
                }
            }
            else
            {
                // SQLite: check if old index exists via sqlite_master
                await using var cmd = _context.Database.GetDbConnection().CreateCommand();
                await _context.Database.OpenConnectionAsync();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_RiskRules_RuleCode'";
                var exists = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;

                if (exists)
                {
                    await _context.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_RiskRules_RuleCode");
                    await _context.Database.ExecuteSqlRawAsync(
                        "CREATE UNIQUE INDEX IX_RiskRules_TenantId_RuleCode ON RiskRules (TenantId, RuleCode)");
                    _logger.LogInformation("Fixed RiskRules index: replaced IX_RiskRules_RuleCode with IX_RiskRules_TenantId_RuleCode");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping RuleCode index fix (table may not exist yet)");
        }
    }

    public string GetActiveDatabaseType()
    {
        var featureFlagsSection = _configuration.GetSection("FeatureFlags");
        var usePostgresStr = featureFlagsSection["PostgresEnabled"];
        var usePostgres = bool.TryParse(usePostgresStr, out var result) && result;
        return usePostgres ? "PostgreSQL" : "SQLite";
    }

    /// <summary>
    /// Ensures the platform owner (from Auth:DefaultUser config) has a TeamMember
    /// record in the default tenant. Without this, the email→tenant lookup in the
    /// auth handlers would not find them and they'd fall back to config defaults.
    /// </summary>
    private async Task EnsurePlatformOwnerTeamMemberAsync()
    {
        try
        {
            var email = _configuration["Auth:DefaultUser"] ?? "compliance_officer@payguard.ai";
            var tenantId = _configuration["MultiTenancy:DefaultTenantId"] ?? "afriex-demo";

            var existing = await _context.TeamMembers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Email.ToLower() == email.ToLower() && t.TenantId == tenantId);

            if (existing == null)
            {
                _context.TeamMembers.Add(new PayGuardAI.Core.Entities.TeamMember
                {
                    TenantId = tenantId,
                    Email = email.Trim().ToLowerInvariant(),
                    DisplayName = "Platform Owner",
                    Role = "SuperAdmin",
                    Status = "active"
                });
                await _context.SaveChangesAsync();
                _logger.LogInformation("Created TeamMember for platform owner {Email} in tenant {Tenant}", email, tenantId);
            }
            else if (existing.Role != "SuperAdmin")
            {
                // Platform owner must always be SuperAdmin — restore if demoted
                _logger.LogWarning("Platform owner {Email} was demoted to {Role} — restoring SuperAdmin", email, existing.Role);
                existing.Role = "SuperAdmin";
                existing.Status = "active";
                await _context.SaveChangesAsync();
                _logger.LogInformation("Restored SuperAdmin role for platform owner {Email}", email);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping platform owner TeamMember creation (table may not exist yet)");
        }
    }

    /// <summary>
    /// Mark existing tenants (pre-onboarding-flag) as onboarded so they don't get
    /// redirected to the onboarding wizard on next login.
    /// </summary>
    private async Task MarkExistingTenantsOnboardedAsync(string dbType)
    {
        try
        {
            // Mark ALL existing tenants as onboarded — they were created before the
            // onboarding wizard existed, so forcing them through it would be disruptive.
            var updateSql = dbType == "PostgreSQL"
                ? """
                   UPDATE "OrganizationSettings"
                   SET "OnboardingCompleted" = true
                   WHERE "OnboardingCompleted" = false
                   """
                : """
                   UPDATE OrganizationSettings
                   SET OnboardingCompleted = 1
                   WHERE OnboardingCompleted = 0
                   """;

            var rows = await _context.Database.ExecuteSqlRawAsync(updateSql);
            if (rows > 0)
                _logger.LogInformation("Marked {Count} existing tenant(s) as onboarded", rows);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping MarkExistingTenantsOnboarded (table may not exist yet)");
        }
    }
}
