#!/usr/bin/env python3
"""Write the clean ApplicationDbContext.cs file."""

content = r'''using Microsoft.EntityFrameworkCore;
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
    /// </summary>
    private string TenantId => _tenantContext?.TenantId ?? "";

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Constructor for migrations and test scenarios where no tenant context is available.
    /// </summary>
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
        _tenantContext = null; // TenantId will return ""
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

        // Entity configuration

        // Transaction configuration
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ExternalId).IsUnique();
            entity.HasIndex(e => e.SenderId);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.TenantId);
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
            entity.HasIndex(e => e.ExternalId).IsUnique();
            entity.HasIndex(e => e.TenantId);
            entity.Property(e => e.TotalVolume).HasPrecision(18, 4);
            entity.Property(e => e.AverageTransactionAmount).HasPrecision(18, 4);
            entity.Property(e => e.MaxTransactionAmount).HasPrecision(18, 4);
        });

        // RiskRule configuration
        modelBuilder.Entity<RiskRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.RuleCode }).IsUnique();
            entity.HasIndex(e => e.IsEnabled);
            entity.Property(e => e.Threshold).HasPrecision(18, 4);
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
                IsEnabled = true
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
                IsEnabled = true
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
                IsEnabled = true
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
                IsEnabled = true
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
                IsEnabled = true
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
                IsEnabled = true
            }
        );

        // NotificationPreference configuration
        modelBuilder.Entity<NotificationPreference>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.Email }).IsUnique();
        });
    }
}
'''

target = '/Users/ebenezer/Desktop/Afriex/PayGuardAI/src/PayGuardAI.Data/ApplicationDbContext.cs'
with open(target, 'w') as f:
    f.write(content)
print(f'Written {len(content)} bytes')
