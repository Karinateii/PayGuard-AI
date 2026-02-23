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
/// Tests for the tenant onboarding service — provisioning, slug generation,
/// duplicate handling, cross-tenant admin queries, and settings updates.
/// </summary>
public class TenantOnboardingTests : IDisposable
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public TenantOnboardingTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"TenantOnboarding_{Guid.NewGuid()}")
            .Options;

        // EnsureCreated triggers HasData seeding (6 global risk rules with TenantId == "")
        using var db = new ApplicationDbContext(_options);
        db.Database.EnsureCreated();
    }

    private ApplicationDbContext CreateDbContext(string tenantId = "")
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(tenantId);
        return new ApplicationDbContext(_options, tenantContext.Object);
    }

    private TenantOnboardingService CreateService(ApplicationDbContext? db = null)
    {
        db ??= CreateDbContext();
        var logger = new Mock<ILogger<TenantOnboardingService>>();
        return new TenantOnboardingService(db, logger.Object);
    }

    // ── Tenant Provisioning ──────────────────────────────────────────────

    [Fact]
    public async Task ProvisionTenantAsync_CreatesAllRequiredEntities()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = CreateService(db);

        // Act
        var result = await service.ProvisionTenantAsync(
            "Acme FinTech",
            "admin@acme.com",
            "Jane Smith");

        // Assert
        result.TenantId.Should().Be("acme-fintech");
        result.Settings.Should().NotBeNull();
        result.Settings.OrganizationName.Should().Be("Acme FinTech");
        result.AdminUser.Should().NotBeNull();
        result.AdminUser.Email.Should().Be("admin@acme.com");
        result.AdminUser.Role.Should().Be("Admin");
        result.AdminUser.Status.Should().Be("active");
        result.Subscription.Should().NotBeNull();
        result.Subscription.Plan.Should().Be(BillingPlan.Trial);
        result.Subscription.Status.Should().Be("trialing");
        result.RulesSeeded.Should().Be(6); // 6 global rules cloned from HasData seed
    }

    [Fact]
    public async Task ProvisionTenantAsync_CreatesOrganizationSettings()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = CreateService(db);

        // Act
        var result = await service.ProvisionTenantAsync(
            "Test Corp",
            "admin@test.com",
            "Admin User");

        // Assert — verify settings in DB
        var settings = await db.OrganizationSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == result.TenantId);

        settings.Should().NotBeNull();
        settings!.OrganizationName.Should().Be("Test Corp");
        settings.SupportEmail.Should().Be("admin@test.com");
        settings.Timezone.Should().Be("UTC");
        settings.DefaultCurrency.Should().Be("USD");
        settings.AutoApproveThreshold.Should().Be(20);
        settings.AutoRejectThreshold.Should().Be(80);
    }

    [Fact]
    public async Task ProvisionTenantAsync_CreatesTrialSubscription_With30DayTrial()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = CreateService(db);

        // Act
        var result = await service.ProvisionTenantAsync(
            "Trial Co",
            "admin@trial.com",
            "Trial Admin");

        // Assert
        var sub = await db.TenantSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == result.TenantId);

        sub.Should().NotBeNull();
        sub!.Plan.Should().Be(BillingPlan.Trial);
        sub.Status.Should().Be("trialing");
        sub.IncludedTransactions.Should().Be(1000);
        sub.TrialEndsAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
        sub.BillingEmail.Should().Be("admin@trial.com");
    }

    [Fact]
    public async Task ProvisionTenantAsync_ClonesGlobalRiskRules()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = CreateService(db);

        // Act
        var result = await service.ProvisionTenantAsync(
            "Rules Test",
            "admin@rules.com",
            "Rules Admin");

        // Assert — tenant should have 3 cloned rules
        var tenantRules = await db.RiskRules
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == result.TenantId)
            .ToListAsync();

        tenantRules.Should().HaveCount(6);
        tenantRules.Should().Contain(r => r.RuleCode == "HIGH_AMOUNT");
        tenantRules.Should().Contain(r => r.RuleCode == "VELOCITY_24H");
        tenantRules.Should().Contain(r => r.RuleCode == "NEW_CUSTOMER");
        tenantRules.Should().Contain(r => r.RuleCode == "HIGH_RISK_CORRIDOR");
        tenantRules.Should().Contain(r => r.RuleCode == "ROUND_AMOUNT");
        tenantRules.Should().Contain(r => r.RuleCode == "UNUSUAL_TIME");

        // Cloned rules should have tenant-specific TenantId (not empty)
        tenantRules.Should().OnlyContain(r => r.TenantId == result.TenantId);
    }

    [Fact]
    public async Task ProvisionTenantAsync_CreatesNotificationPreference()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = CreateService(db);

        // Act
        var result = await service.ProvisionTenantAsync(
            "Notify Corp",
            "admin@notify.com",
            "Notify Admin");

        // Assert
        var pref = await db.Set<NotificationPreference>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == result.TenantId);

        pref.Should().NotBeNull();
        pref!.Email.Should().Be("admin@notify.com");
        pref.RiskAlertsEnabled.Should().BeTrue();
        pref.DailySummaryEnabled.Should().BeTrue();
        pref.MinimumRiskScoreForAlert.Should().Be(50);
    }

    // ── Slug Generation ──────────────────────────────────────────────────

    [Theory]
    [InlineData("Acme FinTech", "acme-fintech")]
    [InlineData("  My  Cool  Company  ", "my-cool-company")]
    [InlineData("Bank-of-Africa", "bank-of-africa")]
    [InlineData("UPPERCASE ORG", "uppercase-org")]
    [InlineData("special@chars#here!", "special-chars-here")]
    [InlineData("123 Numbers First", "123-numbers-first")]
    public async Task ProvisionTenantAsync_GeneratesCorrectSlug(string orgName, string expectedSlug)
    {
        // Arrange — use a unique DB per test to avoid conflicts
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"SlugTest_{Guid.NewGuid()}")
            .Options;

        using var db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns("");
        using var scopedDb = new ApplicationDbContext(options, tenantContext.Object);

        var logger = new Mock<ILogger<TenantOnboardingService>>();
        var service = new TenantOnboardingService(scopedDb, logger.Object);

        // Act
        var result = await service.ProvisionTenantAsync(orgName, "test@test.com", "Test User");

        // Assert
        result.TenantId.Should().Be(expectedSlug);
    }

    [Fact]
    public async Task ProvisionTenantAsync_HandlesDuplicateTenantId_AppendsSuffix()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = CreateService(db);

        // First provision
        var result1 = await service.ProvisionTenantAsync(
            "Duplicate Corp",
            "admin1@dup.com",
            "Admin One");

        // Act — second provision with same org name
        var result2 = await service.ProvisionTenantAsync(
            "Duplicate Corp",
            "admin2@dup.com",
            "Admin Two");

        // Assert — second tenant should have a suffix
        result1.TenantId.Should().Be("duplicate-corp");
        result2.TenantId.Should().NotBe("duplicate-corp");
        result2.TenantId.Should().StartWith("duplicate-corp-");
        result2.TenantId.Length.Should().BeGreaterThan("duplicate-corp".Length);
    }

    // ── Tenant Exists ────────────────────────────────────────────────────

    [Fact]
    public async Task TenantExistsAsync_ReturnsFalse_ForNonExistentTenant()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var exists = await service.TenantExistsAsync("non-existent-tenant");

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task TenantExistsAsync_ReturnsTrue_AfterProvisioning()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        await service.ProvisionTenantAsync("Exists Check", "admin@exists.com", "Admin");

        var exists = await service.TenantExistsAsync("exists-check");

        exists.Should().BeTrue();
    }

    // ── Get All Tenants (Super-Admin) ────────────────────────────────────

    [Fact]
    public async Task GetAllTenantsAsync_ReturnsAllTenantsAcrossTenantBoundaries()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = CreateService(db);

        await service.ProvisionTenantAsync("Alpha Corp", "admin@alpha.com", "Alpha Admin");
        await service.ProvisionTenantAsync("Beta Inc", "admin@beta.com", "Beta Admin");

        // Act
        var tenants = await service.GetAllTenantsAsync();

        // Assert
        tenants.Should().HaveCount(2);
        tenants.Should().Contain(t => t.OrganizationName == "Alpha Corp");
        tenants.Should().Contain(t => t.OrganizationName == "Beta Inc");
    }

    [Fact]
    public async Task GetAllTenantsAsync_IncludesCorrectTeamCount()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = CreateService(db);

        await service.ProvisionTenantAsync("Team Corp", "admin@team.com", "Team Admin");

        // Add another team member directly
        db.TeamMembers.Add(new TeamMember
        {
            TenantId = "team-corp",
            Email = "member@team.com",
            DisplayName = "Team Member",
            Role = "Reviewer",
            Status = "active"
        });
        await db.SaveChangesAsync();

        // Act
        var tenants = await service.GetAllTenantsAsync();

        // Assert
        var teamTenant = tenants.First(t => t.TenantId == "team-corp");
        teamTenant.TeamMemberCount.Should().Be(2); // admin + member
    }

    [Fact]
    public async Task GetAllTenantsAsync_ReturnsOrderedByCreatedAtDescending()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = CreateService(db);

        await service.ProvisionTenantAsync("First Corp", "admin@first.com", "First Admin");
        await Task.Delay(10); // tiny delay for different timestamps
        await service.ProvisionTenantAsync("Second Corp", "admin@second.com", "Second Admin");

        // Act
        var tenants = await service.GetAllTenantsAsync();

        // Assert — most recent first
        tenants[0].OrganizationName.Should().Be("Second Corp");
        tenants[1].OrganizationName.Should().Be("First Corp");
    }

    // ── Set Tenant Status ────────────────────────────────────────────────

    [Fact]
    public async Task SetTenantStatusAsync_DisablesTenant()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = CreateService(db);

        var result = await service.ProvisionTenantAsync("Disable Test", "admin@disable.com", "Admin");

        // Act
        await service.SetTenantStatusAsync(result.TenantId, false);

        // Assert
        var sub = await db.TenantSubscriptions
            .IgnoreQueryFilters()
            .FirstAsync(s => s.TenantId == result.TenantId);

        sub.Status.Should().Be("disabled");
    }

    [Fact]
    public async Task SetTenantStatusAsync_ReenablesTenant()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = CreateService(db);

        var result = await service.ProvisionTenantAsync("Enable Test", "admin@enable.com", "Admin");
        await service.SetTenantStatusAsync(result.TenantId, false); // Disable first

        // Act
        await service.SetTenantStatusAsync(result.TenantId, true);

        // Assert
        var sub = await db.TenantSubscriptions
            .IgnoreQueryFilters()
            .FirstAsync(s => s.TenantId == result.TenantId);

        sub.Status.Should().Be("active");
    }

    // ── Update Onboarding Settings ───────────────────────────────────────

    [Fact]
    public async Task UpdateOnboardingSettingsAsync_UpdatesAllFields()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = CreateService(db);

        var result = await service.ProvisionTenantAsync("Settings Test", "admin@settings.com", "Admin");

        // Act
        await service.UpdateOnboardingSettingsAsync(
            result.TenantId,
            timezone: "Africa/Lagos",
            defaultCurrency: "NGN",
            autoApproveThreshold: 15,
            autoRejectThreshold: 85);

        // Assert
        var settings = await db.OrganizationSettings
            .IgnoreQueryFilters()
            .FirstAsync(s => s.TenantId == result.TenantId);

        settings.Timezone.Should().Be("Africa/Lagos");
        settings.DefaultCurrency.Should().Be("NGN");
        settings.AutoApproveThreshold.Should().Be(15);
        settings.AutoRejectThreshold.Should().Be(85);
    }

    [Fact]
    public async Task UpdateOnboardingSettingsAsync_ThrowsForNonExistentTenant()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = CreateService(db);

        // Act & Assert
        var act = () => service.UpdateOnboardingSettingsAsync(
            "non-existent",
            "UTC", "USD", 20, 80);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*non-existent*");
    }

    // ── Full Onboarding Flow (End-to-End) ────────────────────────────────

    [Fact]
    public async Task FullOnboardingFlow_ProvisionThenConfigure_Succeeds()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = CreateService(db);

        // Step 1: Provision
        var result = await service.ProvisionTenantAsync(
            "Full Flow Corp",
            "admin@fullflow.com",
            "Flow Admin");

        result.TenantId.Should().Be("full-flow-corp");
        result.RulesSeeded.Should().Be(6);

        // Step 2: Verify exists
        var exists = await service.TenantExistsAsync(result.TenantId);
        exists.Should().BeTrue();

        // Step 3: Configure settings (onboarding wizard)
        await service.UpdateOnboardingSettingsAsync(
            result.TenantId,
            "Africa/Nairobi",
            "KES",
            10,
            90);

        // Step 4: Verify final state
        var settings = await db.OrganizationSettings
            .IgnoreQueryFilters()
            .FirstAsync(s => s.TenantId == result.TenantId);

        settings.Timezone.Should().Be("Africa/Nairobi");
        settings.DefaultCurrency.Should().Be("KES");
        settings.AutoApproveThreshold.Should().Be(10);
        settings.AutoRejectThreshold.Should().Be(90);

        // Step 5: Super-admin can see the tenant
        var allTenants = await service.GetAllTenantsAsync();
        allTenants.Should().Contain(t => t.TenantId == result.TenantId);

        // Step 6: Disable and re-enable
        await service.SetTenantStatusAsync(result.TenantId, false);
        var disabled = (await service.GetAllTenantsAsync())
            .First(t => t.TenantId == result.TenantId);
        disabled.Status.Should().Be("disabled");

        await service.SetTenantStatusAsync(result.TenantId, true);
        var reenabled = (await service.GetAllTenantsAsync())
            .First(t => t.TenantId == result.TenantId);
        reenabled.Status.Should().Be("active");
    }

    public void Dispose()
    {
        // InMemoryDatabase is cleaned up automatically per unique DB name
    }
}
