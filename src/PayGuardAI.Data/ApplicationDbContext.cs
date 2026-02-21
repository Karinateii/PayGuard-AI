using Microsoft.EntityFrameworkCore;
using PayGuardAI.Core.Entities;

namespace PayGuardAI.Data;

/// <summary>
/// Application database context for PayGuard AI.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Transaction configuration
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ExternalId).IsUnique();
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
            entity.Property(e => e.TotalVolume).HasPrecision(18, 4);
            entity.Property(e => e.AverageTransactionAmount).HasPrecision(18, 4);
            entity.Property(e => e.MaxTransactionAmount).HasPrecision(18, 4);
        });

        // RiskRule configuration
        modelBuilder.Entity<RiskRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.RuleCode).IsUnique();
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
