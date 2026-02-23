using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;
using PayGuardAI.Data;
using PayGuardAI.Data.Services;

namespace PayGuardAI.Tests.Services;

/// <summary>
/// Tests that verify tenant isolation via EF Core global query filters.
/// Each tenant should only see its own data, and shared rules (TenantId == "") 
/// should be visible to all tenants.
/// </summary>
public class TenantIsolationTests : IDisposable
{
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private const string TenantA = "tenant-alpha";
    private const string TenantB = "tenant-beta";

    public TenantIsolationTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"TenantIsolation_{Guid.NewGuid()}")
            .Options;

        // Seed data using a raw context (bypasses query filter via fallback constructor)
        SeedTestData();
    }

    private ApplicationDbContext CreateDbContext(string tenantId)
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(tenantId);
        return new ApplicationDbContext(_options, tenantContext.Object);
    }

    private void SeedTestData()
    {
        // Use fallback constructor (no query filter scoping for seeding)
        using var db = new ApplicationDbContext(_options);
        db.Database.EnsureCreated();

        // ── Transactions ──────────────────────────────────────────────────
        db.Transactions.AddRange(
            new Transaction
            {
                TenantId = TenantA,
                ExternalId = "txn-a-1",
                Amount = 100m,
                SourceCurrency = "USD",
                DestinationCurrency = "NGN",
                SenderId = "sender-a",
                Type = "WITHDRAWAL",
                Status = "COMPLETED",
                CreatedAt = DateTime.UtcNow,
                ReceivedAt = DateTime.UtcNow,
                RawPayload = "{}"
            },
            new Transaction
            {
                TenantId = TenantA,
                ExternalId = "txn-a-2",
                Amount = 200m,
                SourceCurrency = "USD",
                DestinationCurrency = "GHS",
                SenderId = "sender-a",
                Type = "WITHDRAWAL",
                Status = "COMPLETED",
                CreatedAt = DateTime.UtcNow,
                ReceivedAt = DateTime.UtcNow,
                RawPayload = "{}"
            },
            new Transaction
            {
                TenantId = TenantB,
                ExternalId = "txn-b-1",
                Amount = 500m,
                SourceCurrency = "GBP",
                DestinationCurrency = "KES",
                SenderId = "sender-b",
                Type = "DEPOSIT",
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow,
                ReceivedAt = DateTime.UtcNow,
                RawPayload = "{}"
            }
        );

        // ── Risk Rules (shared + tenant-scoped) ───────────────────────────
        db.RiskRules.AddRange(
            new RiskRule
            {
                TenantId = "", // global/shared rule
                RuleCode = "GLOBAL_RULE",
                Name = "Global Rule",
                Description = "Visible to all tenants",
                Category = "Global",
                Threshold = 1000m,
                ScoreWeight = 20,
                IsEnabled = true
            },
            new RiskRule
            {
                TenantId = TenantA,
                RuleCode = "TENANT_A_RULE",
                Name = "Tenant A Custom Rule",
                Description = "Only visible to Tenant A",
                Category = "Custom",
                Threshold = 500m,
                ScoreWeight = 15,
                IsEnabled = true
            },
            new RiskRule
            {
                TenantId = TenantB,
                RuleCode = "TENANT_B_RULE",
                Name = "Tenant B Custom Rule",
                Description = "Only visible to Tenant B",
                Category = "Custom",
                Threshold = 2000m,
                ScoreWeight = 25,
                IsEnabled = true
            }
        );

        // ── Customer Profiles ─────────────────────────────────────────────
        db.CustomerProfiles.AddRange(
            new CustomerProfile
            {
                TenantId = TenantA,
                ExternalId = "cust-a",
                TotalTransactions = 10,
                TotalVolume = 5000m
            },
            new CustomerProfile
            {
                TenantId = TenantB,
                ExternalId = "cust-b",
                TotalTransactions = 3,
                TotalVolume = 1500m
            }
        );

        // ── Audit Logs ────────────────────────────────────────────────────
        db.AuditLogs.AddRange(
            new AuditLog
            {
                TenantId = TenantA,
                Action = "TEST_ACTION_A",
                EntityType = "Transaction",
                EntityId = "1",
                PerformedBy = "user-a@test.com"
            },
            new AuditLog
            {
                TenantId = TenantB,
                Action = "TEST_ACTION_B",
                EntityType = "Transaction",
                EntityId = "2",
                PerformedBy = "user-b@test.com"
            }
        );

        // ── Team Members ──────────────────────────────────────────────────
        db.TeamMembers.AddRange(
            new TeamMember
            {
                TenantId = TenantA,
                Email = "alice@alpha.com",
                DisplayName = "Alice Alpha",
                Role = "Admin"
            },
            new TeamMember
            {
                TenantId = TenantB,
                Email = "bob@beta.com",
                DisplayName = "Bob Beta",
                Role = "Reviewer"
            }
        );

        db.SaveChanges();
    }

    public void Dispose()
    {
        using var db = new ApplicationDbContext(_options);
        db.Database.EnsureDeleted();
    }

    // ── Transaction Isolation ─────────────────────────────────────────────

    [Fact]
    public async Task TenantA_ShouldOnlySeeItsOwnTransactions()
    {
        using var db = CreateDbContext(TenantA);

        var transactions = await db.Transactions.ToListAsync();

        transactions.Should().HaveCount(2);
        transactions.Should().OnlyContain(t => t.TenantId == TenantA);
    }

    [Fact]
    public async Task TenantB_ShouldOnlySeeItsOwnTransactions()
    {
        using var db = CreateDbContext(TenantB);

        var transactions = await db.Transactions.ToListAsync();

        transactions.Should().ContainSingle();
        transactions[0].TenantId.Should().Be(TenantB);
        transactions[0].ExternalId.Should().Be("txn-b-1");
    }

    [Fact]
    public async Task TenantA_ShouldNotSeeTenantB_Transactions()
    {
        using var db = CreateDbContext(TenantA);

        var transactions = await db.Transactions.ToListAsync();

        transactions.Should().NotContain(t => t.TenantId == TenantB);
        transactions.Should().NotContain(t => t.ExternalId == "txn-b-1");
    }

    // ── Risk Rule Isolation (with shared rules) ───────────────────────────

    [Fact]
    public async Task TenantA_ShouldSeeOwnRules_AndSharedRules()
    {
        using var db = CreateDbContext(TenantA);

        var rules = await db.RiskRules.ToListAsync();

        // Should see: Global Rule + Tenant A Custom Rule + seeded rules (TenantId = "")
        rules.Should().Contain(r => r.RuleCode == "GLOBAL_RULE");
        rules.Should().Contain(r => r.RuleCode == "TENANT_A_RULE");
        rules.Should().NotContain(r => r.RuleCode == "TENANT_B_RULE");
    }

    [Fact]
    public async Task TenantB_ShouldSeeOwnRules_AndSharedRules()
    {
        using var db = CreateDbContext(TenantB);

        var rules = await db.RiskRules.ToListAsync();

        rules.Should().Contain(r => r.RuleCode == "GLOBAL_RULE");
        rules.Should().Contain(r => r.RuleCode == "TENANT_B_RULE");
        rules.Should().NotContain(r => r.RuleCode == "TENANT_A_RULE");
    }

    [Fact]
    public async Task SharedRules_ShouldBeVisibleToAllTenants()
    {
        using var dbA = CreateDbContext(TenantA);
        using var dbB = CreateDbContext(TenantB);

        var rulesA = await dbA.RiskRules.Where(r => r.TenantId == "").ToListAsync();
        var rulesB = await dbB.RiskRules.Where(r => r.TenantId == "").ToListAsync();

        rulesA.Should().NotBeEmpty();
        rulesB.Should().NotBeEmpty();
        rulesA.Select(r => r.RuleCode).Should().BeEquivalentTo(rulesB.Select(r => r.RuleCode));
    }

    // ── Customer Profile Isolation ────────────────────────────────────────

    [Fact]
    public async Task TenantA_ShouldOnlySeeItsOwnCustomers()
    {
        using var db = CreateDbContext(TenantA);

        var profiles = await db.CustomerProfiles.ToListAsync();

        profiles.Should().ContainSingle();
        profiles[0].ExternalId.Should().Be("cust-a");
    }

    [Fact]
    public async Task TenantB_ShouldNotSeeTenantA_Customers()
    {
        using var db = CreateDbContext(TenantB);

        var profiles = await db.CustomerProfiles.ToListAsync();

        profiles.Should().ContainSingle();
        profiles[0].ExternalId.Should().Be("cust-b");
    }

    // ── Audit Log Isolation ───────────────────────────────────────────────

    [Fact]
    public async Task TenantA_ShouldOnlySeeItsOwnAuditLogs()
    {
        using var db = CreateDbContext(TenantA);

        var logs = await db.AuditLogs.ToListAsync();

        logs.Should().ContainSingle();
        logs[0].Action.Should().Be("TEST_ACTION_A");
    }

    [Fact]
    public async Task TenantB_ShouldOnlySeeItsOwnAuditLogs()
    {
        using var db = CreateDbContext(TenantB);

        var logs = await db.AuditLogs.ToListAsync();

        logs.Should().ContainSingle();
        logs[0].Action.Should().Be("TEST_ACTION_B");
    }

    // ── Team Member Isolation ─────────────────────────────────────────────

    [Fact]
    public async Task TenantA_ShouldOnlySeeItsOwnTeamMembers()
    {
        using var db = CreateDbContext(TenantA);

        var members = await db.TeamMembers.ToListAsync();

        members.Should().ContainSingle();
        members[0].Email.Should().Be("alice@alpha.com");
    }

    [Fact]
    public async Task TenantB_ShouldOnlySeeItsOwnTeamMembers()
    {
        using var db = CreateDbContext(TenantB);

        var members = await db.TeamMembers.ToListAsync();

        members.Should().ContainSingle();
        members[0].Email.Should().Be("bob@beta.com");
    }

    // ── Cross-Tenant Write Prevention ─────────────────────────────────────

    [Fact]
    public async Task NewEntityCreatedByTenantA_ShouldNotBeVisibleToTenantB()
    {
        // Tenant A creates a transaction
        using (var dbA = CreateDbContext(TenantA))
        {
            dbA.Transactions.Add(new Transaction
            {
                TenantId = TenantA,
                ExternalId = "txn-a-new",
                Amount = 999m,
                SourceCurrency = "USD",
                DestinationCurrency = "NGN",
                SenderId = "sender-a",
                Type = "WITHDRAWAL",
                Status = "COMPLETED",
                CreatedAt = DateTime.UtcNow,
                ReceivedAt = DateTime.UtcNow,
                RawPayload = "{}"
            });
            await dbA.SaveChangesAsync();
        }

        // Tenant B should not see it
        using (var dbB = CreateDbContext(TenantB))
        {
            var transactions = await dbB.Transactions.ToListAsync();
            transactions.Should().NotContain(t => t.ExternalId == "txn-a-new");
        }
    }

    // ── Middleware Claim Resolution ────────────────────────────────────────

    [Fact]
    public void TenantContext_DefaultValue_ShouldBeEmpty()
    {
        // Default is empty string so that query filters return NO data
        // until TenantCircuitHandler or TenantResolutionMiddleware sets the real tenant.
        // This prevents cross-tenant data leaks on fresh DI scopes.
        var tenantContext = new TenantContext();
        tenantContext.TenantId.Should().Be(string.Empty);
    }

    [Fact]
    public void TenantContext_ShouldBeSettable()
    {
        var tenantContext = new TenantContext();
        tenantContext.TenantId = "custom-tenant";
        tenantContext.TenantId.Should().Be("custom-tenant");
    }
}
