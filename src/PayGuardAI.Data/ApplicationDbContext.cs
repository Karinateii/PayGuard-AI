using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data;

/// <summary>
/// Application database context for PayGuard AI.
/// Applies tenant-scoped global query filters on all entities with TenantId.
/// </summary>
public class ApplicationDbContext : DbContext
{
    private readonly ITenantContext? _tenantContext;

    /// <summary>
    /// Current tenant ID — evaluated at query time, not construction time.
    /// EF Core query filters reference this property so they always use the latest value.
    /// If _overrideTenantId is set (e.g. from factory-created contexts), it takes priority.
    /// </summary>
    private string? _overrideTenantId;
    private string TenantId => _overrideTenantId ?? _tenantContext?.TenantId ?? "";

    /// <summary>
    /// Explicitly set the tenant for this DbContext instance.
    /// Used by IDbContextFactory-created contexts that don't have a scoped ITenantContext.
    /// </summary>
    public void SetTenantId(string tenantId)
    {
        _overrideTenantId = tenantId;
    }

    /// <summary>
    /// Primary constructor — used by DI and IDbContextFactory.
    /// ITenantContext is optional so the factory can create instances without it.
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantContext? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<RiskAnalysis> RiskAnalyses => Set<RiskAnalysis>();
    public DbSet<RiskFactor> RiskFactors => Set<RiskFactor>();
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<RiskRule> RiskRules => Set<RiskRule>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<OrganizationSettings> OrganizationSettings => Set<OrganizationSettings>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<CustomRole> CustomRoles => Set<CustomRole>();
    public DbSet<MagicLinkToken> MagicLinkTokens => Set<MagicLinkToken>();
    public DbSet<CustomReport> CustomReports => Set<CustomReport>();
    public DbSet<MLModel> MLModels => Set<MLModel>();
    public DbSet<RuleTemplate> RuleTemplates => Set<RuleTemplate>();
    public DbSet<RuleGroup> RuleGroups => Set<RuleGroup>();
    public DbSet<RuleGroupCondition> RuleGroupConditions => Set<RuleGroupCondition>();
    public DbSet<RuleVersion> RuleVersions => Set<RuleVersion>();
    public DbSet<GdprRequest> GdprRequests => Set<GdprRequest>();
    public DbSet<Watchlist> Watchlists => Set<Watchlist>();
    public DbSet<WatchlistEntry> WatchlistEntries => Set<WatchlistEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Global tenant query filters
        // Every query is automatically scoped to the current tenant.
        // Use IgnoreQueryFilters() for cross-tenant admin queries.
        modelBuilder.Entity<Transaction>().HasQueryFilter(e => e.TenantId == TenantId);
        modelBuilder.Entity<RiskAnalysis>().HasQueryFilter(e => e.TenantId == TenantId);
        modelBuilder.Entity<RiskFactor>().HasQueryFilter(e => e.TenantId == TenantId);
        modelBuilder.Entity<CustomerProfile>().HasQueryFilter(e => e.TenantId == TenantId);
        modelBuilder.Entity<RiskRule>().HasQueryFilter(e => e.TenantId == TenantId || e.TenantId == "");
        modelBuilder.Entity<AuditLog>().HasQueryFilter(e => e.TenantId == TenantId);
        modelBuilder.Entity<TenantSubscription>().HasQueryFilter(e => e.TenantId == TenantId);
        modelBuilder.Entity<OrganizationSettings>().HasQueryFilter(e => e.TenantId == TenantId);
        modelBuilder.Entity<TeamMember>().HasQueryFilter(e => e.TenantId == TenantId);
        modelBuilder.Entity<ApiKey>().HasQueryFilter(e => e.TenantId == TenantId);
        modelBuilder.Entity<WebhookEndpoint>().HasQueryFilter(e => e.TenantId == TenantId);
        modelBuilder.Entity<NotificationPreference>().HasQueryFilter(e => e.TenantId == TenantId);
        modelBuilder.Entity<CustomRole>().HasQueryFilter(e => e.TenantId == TenantId);
        modelBuilder.Entity<CustomReport>().HasQueryFilter(e => e.TenantId == TenantId);
        modelBuilder.Entity<MLModel>().HasQueryFilter(e => e.TenantId == TenantId);
        modelBuilder.Entity<RuleGroup>().HasQueryFilter(e => e.TenantId == TenantId);
        modelBuilder.Entity<RuleVersion>().HasQueryFilter(e => e.TenantId == TenantId);
        modelBuilder.Entity<GdprRequest>().HasQueryFilter(e => e.TenantId == TenantId);
        modelBuilder.Entity<Watchlist>().HasQueryFilter(e => e.TenantId == TenantId);

        // Entity configuration

        // Transaction configuration
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.ExternalId }).IsUnique();
            entity.HasIndex(e => e.SenderId);
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.Amount).HasPrecision(18, 4);
        });

        // RiskAnalysis configuration
        modelBuilder.Entity<RiskAnalysis>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TransactionId).IsUnique();
            entity.HasIndex(e => e.RiskLevel);
            entity.HasIndex(e => e.ReviewStatus);
            entity.HasIndex(e => e.TenantId);
            
            entity.HasOne(e => e.Transaction)
                  .WithOne(t => t.RiskAnalysis)
                  .HasForeignKey<RiskAnalysis>(e => e.TransactionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // RiskFactor configuration
        modelBuilder.Entity<RiskFactor>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.RiskAnalysisId);
            entity.HasIndex(e => e.TenantId);
            
            entity.HasOne(e => e.RiskAnalysis)
                  .WithMany(r => r.RiskFactors)
                  .HasForeignKey(e => e.RiskAnalysisId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // CustomerProfile configuration
        modelBuilder.Entity<CustomerProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.ExternalId }).IsUnique();
            entity.Property(e => e.TotalVolume).HasPrecision(18, 4);
            entity.Property(e => e.AverageTransactionAmount).HasPrecision(18, 4);
            entity.Property(e => e.MaxTransactionAmount).HasPrecision(18, 4);
        });

        // RiskRule configuration
        modelBuilder.Entity<RiskRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.RuleCode }).IsUnique();
            entity.HasIndex(e => e.Mode);
            entity.Property(e => e.Threshold).HasPrecision(18, 4);
            // IsEnabled is computed from Mode but PostgreSQL has a legacy NOT NULL column.
            // Use the backing field so EF reads/writes _isEnabled directly without
            // calling the property setter (which would override Mode on materialization).
            // NOTE: Do NOT use HasDefaultValue — it tells EF to skip the column on INSERT
            // when the value matches the default, and PostgreSQL has no column DEFAULT.
            entity.Property(e => e.IsEnabled)
                .HasField("_isEnabled")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Ignore(e => e.IsShadow);
        });

        // MLModel configuration
        modelBuilder.Entity<MLModel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.IsActive });
            entity.HasIndex(e => e.TrainedAt);
        });

        // RuleGroup configuration (compound rules)
        modelBuilder.Entity<RuleGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.Mode);
            entity.Ignore(e => e.IsEnabled);
            entity.Ignore(e => e.IsShadow);
        });

        // RuleGroupCondition configuration
        modelBuilder.Entity<RuleGroupCondition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.RuleGroupId);

            entity.HasOne(e => e.RuleGroup)
                  .WithMany(g => g.Conditions)
                  .HasForeignKey(e => e.RuleGroupId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // RuleVersion configuration (rule versioning & rollback)
        modelBuilder.Entity<RuleVersion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.EntityId, e.VersionNumber });
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.CreatedAt);
        });

        // GdprRequest configuration (GDPR compliance audit trail)
        modelBuilder.Entity<GdprRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.SubjectId);
            entity.HasIndex(e => e.RequestedAt);
        });

        // Watchlist configuration
        modelBuilder.Entity<Watchlist>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.Name });
        });

        // WatchlistEntry configuration
        modelBuilder.Entity<WatchlistEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.WatchlistId);
            entity.HasIndex(e => new { e.FieldType, e.Value });
            entity.Ignore(e => e.IsExpired);

            entity.HasOne(e => e.Watchlist)
                  .WithMany(w => w.Entries)
                  .HasForeignKey(e => e.WatchlistId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // AuditLog configuration
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.EntityType);
            entity.HasIndex(e => e.EntityId);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.TenantId);
        });

        // MagicLinkToken configuration (no tenant filter — looked up by token hash)
        modelBuilder.Entity<MagicLinkToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.Email);
            entity.HasIndex(e => e.ExpiresAt);
        });

        // TenantSubscription configuration
        modelBuilder.Entity<TenantSubscription>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId).IsUnique();
            entity.HasIndex(e => e.ProviderCustomerId);
            entity.HasIndex(e => e.ProviderSubscriptionId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Provider);
        });

        // OrganizationSettings configuration
        modelBuilder.Entity<OrganizationSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId).IsUnique();
        });

        // TeamMember configuration
        modelBuilder.Entity<TeamMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.Email }).IsUnique();
            entity.HasIndex(e => e.TenantId);
        });

        // CustomRole configuration
        modelBuilder.Entity<CustomRole>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.Name }).IsUnique();
            entity.HasIndex(e => e.TenantId);
        });

        // ApiKey configuration
        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.KeyHash).IsUnique();
            entity.HasIndex(e => e.TenantId);
            // Store Scopes as comma-separated string for SQLite compatibility
            entity.Property(e => e.Scopes)
                  .HasConversion(
                      v => string.Join(',', v),
                      v => v.Split(',', StringSplitOptions.RemoveEmptyEntries));
        });

        // WebhookEndpoint configuration
        modelBuilder.Entity<WebhookEndpoint>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId);
            // Store Events as comma-separated string for SQLite compatibility
            entity.Property(e => e.Events)
                  .HasConversion(
                      v => string.Join(',', v),
                      v => v.Split(',', StringSplitOptions.RemoveEmptyEntries));
        });

        // Seed default risk rules
        SeedRiskRules(modelBuilder);

        // RuleTemplate configuration — NO query filter (global marketplace catalog)
        modelBuilder.Entity<RuleTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.RuleCode);
            entity.HasIndex(e => e.Industry);
            entity.HasIndex(e => e.IsBuiltIn);
            entity.Property(e => e.Threshold).HasPrecision(18, 4);
            // Store Tags as comma-separated string (SQLite + PostgreSQL compatible)
            entity.Property(e => e.Tags)
                  .HasConversion(
                      v => string.Join(',', v),
                      v => v.Split(',', StringSplitOptions.RemoveEmptyEntries));
        });

        // Seed marketplace rule templates
        SeedRuleTemplates(modelBuilder);
    }

    private static void SeedRiskRules(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiskRule>().HasData(
            new RiskRule
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                RuleCode = "HIGH_AMOUNT",
                Name = "High Transaction Amount",
                Description = "Transaction amount exceeds threshold for the corridor",
                Category = "Amount",
                Threshold = 5000m,
                ScoreWeight = 35,
                Mode = "Active"
            },
            new RiskRule
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                RuleCode = "VELOCITY_24H",
                Name = "High Transaction Velocity (24h)",
                Description = "Multiple transactions in 24-hour window exceeds limit",
                Category = "Velocity",
                Threshold = 5m,
                ScoreWeight = 30,
                Mode = "Active"
            },
            new RiskRule
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                RuleCode = "NEW_CUSTOMER",
                Name = "New Customer Risk",
                Description = "Customer has less than 5 transactions",
                Category = "Pattern",
                Threshold = 5m,
                ScoreWeight = 25,
                Mode = "Active"
            },
            new RiskRule
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                RuleCode = "HIGH_RISK_CORRIDOR",
                Name = "High-Risk Corridor",
                Description = "Transaction involves a high-risk country corridor",
                Category = "Geography",
                Threshold = 1m,
                ScoreWeight = 30,
                Mode = "Active"
            },
            new RiskRule
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                RuleCode = "ROUND_AMOUNT",
                Name = "Suspicious Round Amount",
                Description = "Transaction is an exact round number (potential structuring)",
                Category = "Pattern",
                Threshold = 1000m,
                ScoreWeight = 10,
                Mode = "Active"
            },
            new RiskRule
            {
                Id = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                RuleCode = "UNUSUAL_TIME",
                Name = "Unusual Transaction Time",
                Description = "Transaction occurs outside normal hours for the region",
                Category = "Pattern",
                Threshold = 1m,
                ScoreWeight = 10,
                Mode = "Active"
            }
        );

        // NotificationPreference configuration
        modelBuilder.Entity<NotificationPreference>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.Email }).IsUnique();
        });

        // CustomReport configuration (Advanced Analytics)
        modelBuilder.Entity<CustomReport>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.CreatedBy });
        });
    }

    private static void SeedRuleTemplates(ModelBuilder modelBuilder)
    {
        var now = new DateTime(2026, 2, 26, 0, 0, 0, DateTimeKind.Utc);

        modelBuilder.Entity<RuleTemplate>().HasData(

            // ── Remittance Pack ──────────────────────────────────────
            new RuleTemplate
            {
                Id = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001"),
                Name = "Remittance: Large Transfer Alert",
                Description = "Optimized for cross-border remittance. Flags transfers above $10,000 — the threshold where most jurisdictions require enhanced due diligence under AML regulations.",
                RuleCode = "HIGH_AMOUNT", Category = "Amount",
                Threshold = 10000m, ScoreWeight = 35,
                Industry = "Remittance", Tags = new[] { "cross-border", "aml", "high-value" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("aaaaaaaa-0002-4000-8000-000000000002"),
                Name = "Remittance: Rapid Send Detection",
                Description = "Flags senders with 3+ transfers in 24 hours. Legitimate remittance users rarely send more than 1-2 times per day.",
                RuleCode = "VELOCITY_24H", Category = "Velocity",
                Threshold = 3m, ScoreWeight = 35,
                Industry = "Remittance", Tags = new[] { "velocity", "structuring", "smurfing" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("aaaaaaaa-0003-4000-8000-000000000003"),
                Name = "Remittance: New Sender Screening",
                Description = "Enhanced scrutiny for senders with fewer than 3 prior transactions. First-time remittance senders carry higher fraud risk.",
                RuleCode = "NEW_CUSTOMER", Category = "Pattern",
                Threshold = 3m, ScoreWeight = 30,
                Industry = "Remittance", Tags = new[] { "new-customer", "onboarding", "kyc" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("aaaaaaaa-0004-4000-8000-000000000004"),
                Name = "Remittance: Sanctioned Corridor Check",
                Description = "Critical check for transfers involving OFAC-sanctioned corridors (IR, KP, SY, YE, VE, CU). Mandatory for all licensed money transmitters.",
                RuleCode = "HIGH_RISK_CORRIDOR", Category = "Geography",
                Threshold = 1m, ScoreWeight = 40,
                Industry = "Remittance", Tags = new[] { "sanctions", "ofac", "compliance" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("aaaaaaaa-0005-4000-8000-000000000005"),
                Name = "Remittance: Structuring Detection",
                Description = "Detects round amounts above $500 that may indicate structuring (splitting transfers to avoid reporting thresholds).",
                RuleCode = "ROUND_AMOUNT", Category = "Pattern",
                Threshold = 500m, ScoreWeight = 15,
                Industry = "Remittance", Tags = new[] { "structuring", "sar", "ctr" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("aaaaaaaa-0006-4000-8000-000000000006"),
                Name = "Remittance: Off-Hours Transfer",
                Description = "Flags transfers between 2-5 AM UTC. Legitimate remittance users rarely initiate transfers at these hours.",
                RuleCode = "UNUSUAL_TIME", Category = "Pattern",
                Threshold = 1m, ScoreWeight = 15,
                Industry = "Remittance", Tags = new[] { "off-hours", "behavioral" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },

            // ── E-Commerce Pack ──────────────────────────────────────
            new RuleTemplate
            {
                Id = Guid.Parse("bbbbbbbb-0001-4000-8000-000000000001"),
                Name = "E-Commerce: High-Value Purchase",
                Description = "Flags online purchases above $2,000. E-commerce fraud skews toward high-value single orders with stolen cards.",
                RuleCode = "HIGH_AMOUNT", Category = "Amount",
                Threshold = 2000m, ScoreWeight = 30,
                Industry = "E-Commerce", Tags = new[] { "card-fraud", "chargeback", "high-value" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("bbbbbbbb-0002-4000-8000-000000000002"),
                Name = "E-Commerce: Rapid Purchase Velocity",
                Description = "Allows up to 15 orders/day before flagging. E-commerce has legitimately higher velocity than remittance.",
                RuleCode = "VELOCITY_24H", Category = "Velocity",
                Threshold = 15m, ScoreWeight = 25,
                Industry = "E-Commerce", Tags = new[] { "bot-detection", "card-testing" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("bbbbbbbb-0003-4000-8000-000000000003"),
                Name = "E-Commerce: First-Time Buyer",
                Description = "Strict scrutiny for first-time buyers (fewer than 2 orders). Account takeover and stolen card fraud heavily target new accounts.",
                RuleCode = "NEW_CUSTOMER", Category = "Pattern",
                Threshold = 2m, ScoreWeight = 35,
                Industry = "E-Commerce", Tags = new[] { "first-purchase", "account-takeover" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("bbbbbbbb-0004-4000-8000-000000000004"),
                Name = "E-Commerce: Cross-Border Purchase",
                Description = "Moderate weight for cross-border e-commerce — common for legitimate buyers but also for carding rings.",
                RuleCode = "HIGH_RISK_CORRIDOR", Category = "Geography",
                Threshold = 1m, ScoreWeight = 25,
                Industry = "E-Commerce", Tags = new[] { "cross-border", "carding" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("bbbbbbbb-0005-4000-8000-000000000005"),
                Name = "E-Commerce: Round Amount Alert",
                Description = "Flags round amounts above $100. Gift card fraud and card testing often use exact round numbers.",
                RuleCode = "ROUND_AMOUNT", Category = "Pattern",
                Threshold = 100m, ScoreWeight = 20,
                Industry = "E-Commerce", Tags = new[] { "gift-card", "card-testing" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("bbbbbbbb-0006-4000-8000-000000000006"),
                Name = "E-Commerce: Late-Night Purchase",
                Description = "Low weight for off-hours purchases. Online shopping is 24/7 so time is less indicative of fraud.",
                RuleCode = "UNUSUAL_TIME", Category = "Pattern",
                Threshold = 1m, ScoreWeight = 10,
                Industry = "E-Commerce", Tags = new[] { "behavioral", "time-based" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },

            // ── Micro-Lending Pack ───────────────────────────────────
            new RuleTemplate
            {
                Id = Guid.Parse("cccccccc-0001-4000-8000-000000000001"),
                Name = "Lending: Large Loan Application",
                Description = "Flags loan applications above $5,000. Higher loan amounts carry proportionally higher default and fraud risk.",
                RuleCode = "HIGH_AMOUNT", Category = "Amount",
                Threshold = 5000m, ScoreWeight = 30,
                Industry = "Lending", Tags = new[] { "loan-fraud", "default-risk" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("cccccccc-0002-4000-8000-000000000002"),
                Name = "Lending: Multiple Applications",
                Description = "Flags 2+ loan applications in 24 hours. Rapid applications across lenders is a strong indicator of loan stacking fraud.",
                RuleCode = "VELOCITY_24H", Category = "Velocity",
                Threshold = 2m, ScoreWeight = 40,
                Industry = "Lending", Tags = new[] { "loan-stacking", "application-fraud" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("cccccccc-0003-4000-8000-000000000003"),
                Name = "Lending: First-Time Borrower",
                Description = "Maximum scrutiny for first-time borrowers (0 prior transactions). Identity fraud disproportionately targets new accounts.",
                RuleCode = "NEW_CUSTOMER", Category = "Pattern",
                Threshold = 1m, ScoreWeight = 40,
                Industry = "Lending", Tags = new[] { "identity-fraud", "synthetic-identity" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("cccccccc-0004-4000-8000-000000000004"),
                Name = "Lending: Sanctioned Country",
                Description = "Moderate weight for sanctioned corridor checks. Lending is typically domestic, so cross-border is inherently unusual.",
                RuleCode = "HIGH_RISK_CORRIDOR", Category = "Geography",
                Threshold = 1m, ScoreWeight = 25,
                Industry = "Lending", Tags = new[] { "sanctions", "compliance" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("cccccccc-0005-4000-8000-000000000005"),
                Name = "Lending: Round Loan Amount",
                Description = "Low weight — loan amounts are often round by nature. Only flags amounts at $1,000 intervals.",
                RuleCode = "ROUND_AMOUNT", Category = "Pattern",
                Threshold = 1000m, ScoreWeight = 10,
                Industry = "Lending", Tags = new[] { "low-signal" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("cccccccc-0006-4000-8000-000000000006"),
                Name = "Lending: Off-Hours Application",
                Description = "Moderate weight for loan applications filed between 2-5 AM. Automated fraud bots often operate during off-peak hours.",
                RuleCode = "UNUSUAL_TIME", Category = "Pattern",
                Threshold = 1m, ScoreWeight = 20,
                Industry = "Lending", Tags = new[] { "bot-detection", "behavioral" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },

            // ── Crypto / DeFi Pack ──────────────────────────────────
            new RuleTemplate
            {
                Id = Guid.Parse("dddddddd-0001-4000-8000-000000000001"),
                Name = "Crypto: Whale Transaction",
                Description = "High threshold ($50,000) reflects that large crypto transfers are common. Only the largest transactions warrant extra scrutiny.",
                RuleCode = "HIGH_AMOUNT", Category = "Amount",
                Threshold = 50000m, ScoreWeight = 25,
                Industry = "Crypto", Tags = new[] { "whale", "large-transfer" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("dddddddd-0002-4000-8000-000000000002"),
                Name = "Crypto: Rapid Trading",
                Description = "Allows up to 10 transactions/day — active trading is normal. Flags velocity indicative of automated laundering.",
                RuleCode = "VELOCITY_24H", Category = "Velocity",
                Threshold = 10m, ScoreWeight = 30,
                Industry = "Crypto", Tags = new[] { "automated", "wash-trading" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("dddddddd-0003-4000-8000-000000000003"),
                Name = "Crypto: New Wallet",
                Description = "Standard threshold (5 transactions) for new wallets. Crypto users frequently create new addresses.",
                RuleCode = "NEW_CUSTOMER", Category = "Pattern",
                Threshold = 5m, ScoreWeight = 25,
                Industry = "Crypto", Tags = new[] { "new-wallet", "pseudonymous" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("dddddddd-0004-4000-8000-000000000004"),
                Name = "Crypto: OFAC Corridor",
                Description = "Maximum weight for OFAC-sanctioned corridors. Travel Rule and FATF compliance are critical for crypto VASPs.",
                RuleCode = "HIGH_RISK_CORRIDOR", Category = "Geography",
                Threshold = 1m, ScoreWeight = 40,
                Industry = "Crypto", Tags = new[] { "ofac", "travel-rule", "fatf" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("dddddddd-0005-4000-8000-000000000005"),
                Name = "Crypto: Round Amount Pattern",
                Description = "Higher threshold ($10,000) — crypto amounts are often unround due to exchange rates. Round amounts stand out more.",
                RuleCode = "ROUND_AMOUNT", Category = "Pattern",
                Threshold = 10000m, ScoreWeight = 15,
                Industry = "Crypto", Tags = new[] { "structuring", "layering" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            },
            new RuleTemplate
            {
                Id = Guid.Parse("dddddddd-0006-4000-8000-000000000006"),
                Name = "Crypto: Always-On Market",
                Description = "Minimal weight — crypto markets operate 24/7 so time-of-day is a weak fraud signal.",
                RuleCode = "UNUSUAL_TIME", Category = "Pattern",
                Threshold = 1m, ScoreWeight = 5,
                Industry = "Crypto", Tags = new[] { "24-7", "low-signal" },
                IsBuiltIn = true, Author = "PayGuard AI", Version = "1.0",
                CreatedAt = now, UpdatedAt = now
            }
        );
    }
}
