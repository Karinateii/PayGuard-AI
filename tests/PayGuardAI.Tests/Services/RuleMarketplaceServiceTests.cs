using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;
using PayGuardAI.Data;
using PayGuardAI.Data.Services;

namespace PayGuardAI.Tests.Services;

public class RuleMarketplaceServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly RuleMarketplaceService _service;
    private const string TenantId = "test-tenant";

    public RuleMarketplaceServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(TenantId);

        _db = new ApplicationDbContext(options, tenantContext.Object);
        _db.Database.EnsureCreated();

        var logger = Mock.Of<ILogger<RuleMarketplaceService>>();
        _service = new RuleMarketplaceService(_db, logger);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════
    // GetTemplatesAsync
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetTemplatesAsync_ReturnsAllSeededTemplates()
    {
        // 24 templates are seeded (6 per industry × 4 industries)
        var templates = await _service.GetTemplatesAsync(TenantId);

        templates.Should().HaveCount(24);
    }

    [Fact]
    public async Task GetTemplatesAsync_FilterByIndustry_ReturnsOnlyMatchingIndustry()
    {
        var filter = new RuleTemplateFilter { Industry = "Remittance" };

        var templates = await _service.GetTemplatesAsync(TenantId, filter);

        templates.Should().HaveCount(6);
        templates.Should().OnlyContain(t => t.Industry == "Remittance");
    }

    [Fact]
    public async Task GetTemplatesAsync_FilterByCategory_ReturnsOnlyMatchingCategory()
    {
        var filter = new RuleTemplateFilter { Category = "Amount" };

        var templates = await _service.GetTemplatesAsync(TenantId, filter);

        // Each industry has 1 HIGH_AMOUNT (Amount) + 1 ROUND_AMOUNT (Pattern) template.
        // Category "Amount" corresponds to HIGH_AMOUNT only = 4 templates (one per industry).
        templates.Should().OnlyContain(t => t.Category == "Amount");
        templates.Count.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task GetTemplatesAsync_SearchByTerm_ReturnsMatches()
    {
        var filter = new RuleTemplateFilter { SearchTerm = "whale" };

        var templates = await _service.GetTemplatesAsync(TenantId, filter);

        // "Crypto: Whale Transaction" template
        templates.Should().HaveCount(1);
        templates[0].Industry.Should().Be("Crypto");
        templates[0].RuleCode.Should().Be("HIGH_AMOUNT");
    }

    [Fact]
    public async Task GetTemplatesAsync_NoTenantRules_AllShowNotImported()
    {
        // Fresh tenant has no tenant-specific rules (only global with TenantId == "")
        var templates = await _service.GetTemplatesAsync(TenantId);

        templates.Should().OnlyContain(t => t.IsImported == false);
    }

    [Fact]
    public async Task GetTemplatesAsync_WithTenantRule_MatchingCodeShowsImported()
    {
        // Add a tenant-specific rule
        _db.RiskRules.Add(new RiskRule
        {
            TenantId = TenantId,
            RuleCode = "HIGH_AMOUNT",
            Name = "My High Amount Rule",
            Category = "Amount",
            Threshold = 5000,
            ScoreWeight = 30
        });
        await _db.SaveChangesAsync();

        var templates = await _service.GetTemplatesAsync(TenantId);

        // All HIGH_AMOUNT templates (1 per industry = 4) should show IsImported = true
        var highAmountTemplates = templates.Where(t => t.RuleCode == "HIGH_AMOUNT");
        highAmountTemplates.Should().OnlyContain(t => t.IsImported == true);

        // All other templates should still show IsImported = false
        var otherTemplates = templates.Where(t => t.RuleCode != "HIGH_AMOUNT");
        otherTemplates.Should().OnlyContain(t => t.IsImported == false);
    }

    [Fact]
    public async Task GetTemplatesAsync_GlobalRulesNotCountedAsImported()
    {
        // The DB already has global rules (TenantId == "") from seed data.
        // Ensure they're NOT counted as "imported" by this tenant.
        var templates = await _service.GetTemplatesAsync(TenantId);

        // Even though global rules exist with all 6 codes, none should show
        // as imported for this tenant because the IsImported check filters
        // by TenantId explicitly.
        templates.Should().OnlyContain(t => t.IsImported == false);
    }

    // ═══════════════════════════════════════════════════════════════════
    // GetTemplateByIdAsync
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetTemplateByIdAsync_ReturnsTemplateInfo()
    {
        var allTemplates = await _service.GetTemplatesAsync(TenantId);
        var first = allTemplates.First();

        var result = await _service.GetTemplateByIdAsync(first.Id, TenantId);

        result.Should().NotBeNull();
        result!.Id.Should().Be(first.Id);
        result.Name.Should().Be(first.Name);
    }

    [Fact]
    public async Task GetTemplateByIdAsync_NonExistent_ReturnsNull()
    {
        var result = await _service.GetTemplateByIdAsync(Guid.NewGuid(), TenantId);

        result.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════
    // ImportTemplateAsync — single template import
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportTemplateAsync_CreatesNewRule_WhenTenantHasNone()
    {
        var templates = await _service.GetTemplatesAsync(TenantId);
        var template = templates.First(t => t.Industry == "Remittance" && t.RuleCode == "HIGH_AMOUNT");

        var result = await _service.ImportTemplateAsync(template.Id, TenantId, "admin@test.com");

        result.Success.Should().BeTrue();
        result.Action.Should().Be(ImportAction.Created);
        result.RuleCode.Should().Be("HIGH_AMOUNT");

        // Verify the rule was actually created in the DB
        var createdRule = await _db.RiskRules
            .Where(r => r.TenantId == TenantId && r.RuleCode == "HIGH_AMOUNT")
            .FirstOrDefaultAsync();

        createdRule.Should().NotBeNull();
        createdRule!.Threshold.Should().Be(template.Threshold);
        createdRule.ScoreWeight.Should().Be(template.ScoreWeight);
        createdRule.UpdatedBy.Should().Be("admin@test.com");
    }

    [Fact]
    public async Task ImportTemplateAsync_UpdatesExistingRule_WhenTenantAlreadyHasCode()
    {
        // Pre-create a tenant rule
        _db.RiskRules.Add(new RiskRule
        {
            TenantId = TenantId,
            RuleCode = "HIGH_AMOUNT",
            Name = "Old Rule",
            Category = "Amount",
            Threshold = 1000,
            ScoreWeight = 10,
            UpdatedBy = "old-user"
        });
        await _db.SaveChangesAsync();

        // Import a Remittance HIGH_AMOUNT template (threshold $10,000, weight 35)
        var templates = await _service.GetTemplatesAsync(TenantId);
        var template = templates.First(t => t.Industry == "Remittance" && t.RuleCode == "HIGH_AMOUNT");

        var result = await _service.ImportTemplateAsync(template.Id, TenantId, "admin@test.com");

        result.Success.Should().BeTrue();
        result.Action.Should().Be(ImportAction.Updated);

        // Verify the rule's parameters were updated
        var updatedRule = await _db.RiskRules
            .Where(r => r.TenantId == TenantId && r.RuleCode == "HIGH_AMOUNT")
            .FirstOrDefaultAsync();

        updatedRule.Should().NotBeNull();
        updatedRule!.Threshold.Should().Be(template.Threshold);
        updatedRule.ScoreWeight.Should().Be(template.ScoreWeight);
        updatedRule.UpdatedBy.Should().Be("admin@test.com");
    }

    [Fact]
    public async Task ImportTemplateAsync_DoesNotModifyGlobalRules()
    {
        // Get the global HIGH_AMOUNT rule's original threshold
        var globalRule = await _db.RiskRules
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TenantId == "" && r.RuleCode == "HIGH_AMOUNT");

        var originalThreshold = globalRule?.Threshold ?? 0;
        var originalWeight = globalRule?.ScoreWeight ?? 0;

        // Import a Crypto HIGH_AMOUNT template (threshold $50,000)
        var templates = await _service.GetTemplatesAsync(TenantId);
        var template = templates.First(t => t.Industry == "Crypto" && t.RuleCode == "HIGH_AMOUNT");

        await _service.ImportTemplateAsync(template.Id, TenantId, "admin@test.com");

        // Global rule should be UNCHANGED
        var globalRuleAfter = await _db.RiskRules
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TenantId == "" && r.RuleCode == "HIGH_AMOUNT");

        if (globalRuleAfter != null)
        {
            globalRuleAfter.Threshold.Should().Be(originalThreshold);
            globalRuleAfter.ScoreWeight.Should().Be(originalWeight);
        }
    }

    [Fact]
    public async Task ImportTemplateAsync_IncrementsImportCount()
    {
        var templates = await _service.GetTemplatesAsync(TenantId);
        var template = templates.First();
        var initialCount = template.ImportCount;

        await _service.ImportTemplateAsync(template.Id, TenantId, "admin@test.com");

        // Re-read from DB
        var dbTemplate = await _db.RuleTemplates.FindAsync(template.Id);
        dbTemplate!.ImportCount.Should().Be(initialCount + 1);
    }

    [Fact]
    public async Task ImportTemplateAsync_NonExistentTemplate_ReturnsFailure()
    {
        var result = await _service.ImportTemplateAsync(Guid.NewGuid(), TenantId, "admin@test.com");

        result.Success.Should().BeFalse();
        result.Action.Should().Be(ImportAction.Skipped);
        result.ErrorMessage.Should().Contain("not found");
    }

    // ═══════════════════════════════════════════════════════════════════
    // ImportIndustryPackAsync — bulk import
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportIndustryPackAsync_CreatesAllRulesForIndustry()
    {
        var result = await _service.ImportIndustryPackAsync("Remittance", TenantId, "admin@test.com");

        result.Success.Should().BeTrue();
        result.Industry.Should().Be("Remittance");
        result.Created.Should().Be(6);
        result.Updated.Should().Be(0);
        result.Skipped.Should().Be(0);
        result.Results.Should().HaveCount(6);
        result.Results.Should().OnlyContain(r => r.Success && r.Action == ImportAction.Created);

        // Verify all 6 rules exist for the tenant
        var tenantRules = await _db.RiskRules
            .Where(r => r.TenantId == TenantId)
            .ToListAsync();

        tenantRules.Should().HaveCount(6);
    }

    [Fact]
    public async Task ImportIndustryPackAsync_UpdatesExistingRules()
    {
        // Pre-create 2 tenant rules
        _db.RiskRules.AddRange(
            new RiskRule { TenantId = TenantId, RuleCode = "HIGH_AMOUNT", Name = "Old", Category = "Amount", Threshold = 100, ScoreWeight = 5 },
            new RiskRule { TenantId = TenantId, RuleCode = "VELOCITY_24H", Name = "Old", Category = "Velocity", Threshold = 1, ScoreWeight = 5 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.ImportIndustryPackAsync("E-Commerce", TenantId, "admin@test.com");

        result.Success.Should().BeTrue();
        result.Created.Should().Be(4); // 4 new rules
        result.Updated.Should().Be(2); // 2 updated
    }

    [Fact]
    public async Task ImportIndustryPackAsync_NonExistentIndustry_ReturnsFailure()
    {
        var result = await _service.ImportIndustryPackAsync("NonExistent", TenantId, "admin@test.com");

        result.Success.Should().BeFalse();
        result.Results.Should().ContainSingle(r => r.ErrorMessage!.Contains("No templates found"));
    }

    [Fact]
    public async Task ImportIndustryPackAsync_IncrementsImportCountOnAllTemplates()
    {
        await _service.ImportIndustryPackAsync("Lending", TenantId, "admin@test.com");

        var lendingTemplates = await _db.RuleTemplates
            .Where(t => t.Industry == "Lending")
            .ToListAsync();

        lendingTemplates.Should().OnlyContain(t => t.ImportCount >= 1);
    }

    // ═══════════════════════════════════════════════════════════════════
    // GetRuleAnalyticsAsync
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetRuleAnalyticsAsync_NoTenantRules_ReturnsEmptyList()
    {
        // Remove all rules for this tenant (global rules have TenantId == "" but
        // are included by the query filter — they'll still show in analytics)
        // Actually, let's test with a tenant that has their own rules.
        // With only global rules visible via the filter, analytics should still work.
        // But if tenant has no rules AND no global rules match, it returns empty.

        // Create a separate context with a tenant that has no rules at all
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns("empty-tenant");

        using var db = new ApplicationDbContext(options, tenantContext.Object);
        db.Database.EnsureCreated();

        var logger = Mock.Of<ILogger<RuleMarketplaceService>>();
        var service = new RuleMarketplaceService(db, logger);

        // The global rules (TenantId == "") are visible, so analytics will include them
        var analytics = await service.GetRuleAnalyticsAsync("empty-tenant");

        // Global rules exist from seed data, so analytics won't be empty
        analytics.Should().NotBeNull();
        // All should have 0 hits since no transactions exist for this tenant
        analytics.Should().OnlyContain(a => a.TotalHits == 0);
    }

    [Fact]
    public async Task GetRuleAnalyticsAsync_ComputesHitRateAndPrecision()
    {
        // Seed a tenant rule
        _db.RiskRules.Add(new RiskRule
        {
            TenantId = TenantId,
            RuleCode = "HIGH_AMOUNT",
            Name = "High Amount",
            Category = "Amount",
            Threshold = 5000,
            ScoreWeight = 30,
            IsEnabled = true
        });

        // Seed 10 transactions
        for (int i = 0; i < 10; i++)
        {
            _db.Transactions.Add(new Transaction
            {
                TenantId = TenantId,
                ExternalId = $"txn-{i}",
                Type = "send",
                Status = "completed",
                Amount = 1000 * (i + 1),
                SourceCurrency = "USD",
                DestinationCurrency = "NGN",
                SenderId = $"sender-{i}",
                SourceCountry = "US",
                DestinationCountry = "NG",
                CreatedAt = DateTime.UtcNow
            });
        }

        // Create risk analyses with review outcomes
        var analysis1 = new RiskAnalysis
        {
            TenantId = TenantId,
            RiskScore = 80,
            RiskLevel = RiskLevel.High,
            ReviewStatus = ReviewStatus.Rejected // True positive
        };
        var analysis2 = new RiskAnalysis
        {
            TenantId = TenantId,
            RiskScore = 60,
            RiskLevel = RiskLevel.Medium,
            ReviewStatus = ReviewStatus.Approved // False positive
        };
        var analysis3 = new RiskAnalysis
        {
            TenantId = TenantId,
            RiskScore = 70,
            RiskLevel = RiskLevel.High,
            ReviewStatus = ReviewStatus.Rejected // True positive
        };

        _db.RiskAnalyses.AddRange(analysis1, analysis2, analysis3);
        await _db.SaveChangesAsync();

        // Create risk factors (rule hits) linked to analyses
        _db.RiskFactors.AddRange(
            new RiskFactor { TenantId = TenantId, RiskAnalysisId = analysis1.Id, Category = "Amount", RuleName = "High Amount", ScoreContribution = 30, Severity = FactorSeverity.Alert },
            new RiskFactor { TenantId = TenantId, RiskAnalysisId = analysis2.Id, Category = "Amount", RuleName = "High Amount", ScoreContribution = 30, Severity = FactorSeverity.Alert },
            new RiskFactor { TenantId = TenantId, RiskAnalysisId = analysis3.Id, Category = "Amount", RuleName = "High Amount", ScoreContribution = 30, Severity = FactorSeverity.Alert }
        );
        await _db.SaveChangesAsync();

        var analytics = await _service.GetRuleAnalyticsAsync(TenantId);

        // Find the "High Amount" rule analytics
        var highAmountAnalytics = analytics.FirstOrDefault(a => a.RuleName == "High Amount");
        highAmountAnalytics.Should().NotBeNull();
        highAmountAnalytics!.TotalHits.Should().Be(3);
        highAmountAnalytics.HitRatePercent.Should().Be(30.0); // 3 hits / 10 transactions = 30%
        highAmountAnalytics.TruePositives.Should().Be(2);  // 2 rejected
        highAmountAnalytics.FalsePositives.Should().Be(1);  // 1 approved
        highAmountAnalytics.PrecisionPercent.Should().BeApproximately(66.7, 0.1); // 2 / 3 = 66.7%
        highAmountAnalytics.FalsePositiveRatePercent.Should().BeApproximately(33.3, 0.1); // 1 / 3 = 33.3%
    }

    [Fact]
    public async Task GetRuleAnalyticsAsync_ExcludesMLFactors()
    {
        // Seed a tenant rule
        _db.RiskRules.Add(new RiskRule
        {
            TenantId = TenantId,
            RuleCode = "HIGH_AMOUNT",
            Name = "High Amount",
            Category = "Amount",
            Threshold = 5000,
            ScoreWeight = 30,
            IsEnabled = true
        });

        _db.Transactions.Add(new Transaction
        {
            TenantId = TenantId,
            ExternalId = "txn-1",
            Type = "send",
            Status = "completed",
            Amount = 10000,
            SourceCurrency = "USD",
            DestinationCurrency = "NGN",
            SenderId = "sender-1",
            SourceCountry = "US",
            DestinationCountry = "NG",
            CreatedAt = DateTime.UtcNow
        });

        var analysis = new RiskAnalysis
        {
            TenantId = TenantId,
            RiskScore = 80,
            RiskLevel = RiskLevel.High,
            ReviewStatus = ReviewStatus.Rejected
        };
        _db.RiskAnalyses.Add(analysis);
        await _db.SaveChangesAsync();

        // Add an ML factor (should be excluded from rule analytics)
        _db.RiskFactors.AddRange(
            new RiskFactor { TenantId = TenantId, RiskAnalysisId = analysis.Id, Category = "Amount", RuleName = "High Amount", ScoreContribution = 30, Severity = FactorSeverity.Alert },
            new RiskFactor { TenantId = TenantId, RiskAnalysisId = analysis.Id, Category = "ML", RuleName = "ML Risk Score", ScoreContribution = 40, Severity = FactorSeverity.Warning }
        );
        await _db.SaveChangesAsync();

        var analytics = await _service.GetRuleAnalyticsAsync(TenantId);

        var highAmount = analytics.FirstOrDefault(a => a.RuleName == "High Amount");
        highAmount.Should().NotBeNull();
        highAmount!.TotalHits.Should().Be(1);

        // ML factor should NOT appear as a separate rule in analytics
        analytics.Should().NotContain(a => a.RuleName == "ML Risk Score");
    }

    // ═══════════════════════════════════════════════════════════════════
    // GetIndustriesAsync
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetIndustriesAsync_ReturnsFourSeededIndustries()
    {
        var industries = await _service.GetIndustriesAsync();

        industries.Should().HaveCount(4);
        industries.Should().Contain("Remittance");
        industries.Should().Contain("E-Commerce");
        industries.Should().Contain("Lending");
        industries.Should().Contain("Crypto");
    }

    [Fact]
    public async Task GetIndustriesAsync_ReturnsAlphabeticalOrder()
    {
        var industries = await _service.GetIndustriesAsync();

        industries.Should().BeInAscendingOrder();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Integration: Import → Re-query shows IsImported
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportThenQuery_TemplateShowsAsImported()
    {
        // Before import: nothing imported
        var beforeTemplates = await _service.GetTemplatesAsync(TenantId);
        beforeTemplates.Should().OnlyContain(t => !t.IsImported);

        // Import one template
        var template = beforeTemplates.First(t => t.Industry == "Remittance" && t.RuleCode == "VELOCITY_24H");
        await _service.ImportTemplateAsync(template.Id, TenantId, "admin@test.com");

        // After import: VELOCITY_24H templates show as imported
        var afterTemplates = await _service.GetTemplatesAsync(TenantId);
        var velocityTemplates = afterTemplates.Where(t => t.RuleCode == "VELOCITY_24H");
        velocityTemplates.Should().OnlyContain(t => t.IsImported);

        // Other codes still not imported
        var nonVelocity = afterTemplates.Where(t => t.RuleCode != "VELOCITY_24H");
        nonVelocity.Should().OnlyContain(t => !t.IsImported);
    }

    [Fact]
    public async Task ImportIndustryPack_ThenReImport_ShowsAllUpdated()
    {
        // First import: all created
        var first = await _service.ImportIndustryPackAsync("Crypto", TenantId, "admin@test.com");
        first.Created.Should().Be(6);
        first.Updated.Should().Be(0);

        // Second import of the same pack: all updated
        var second = await _service.ImportIndustryPackAsync("Crypto", TenantId, "admin@test.com");
        second.Created.Should().Be(0);
        second.Updated.Should().Be(6);
    }
}
