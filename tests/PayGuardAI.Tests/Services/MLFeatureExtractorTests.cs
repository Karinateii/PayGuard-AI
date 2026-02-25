using FluentAssertions;
using PayGuardAI.Core.Entities;
using PayGuardAI.Data.ML;

namespace PayGuardAI.Tests.Services;

/// <summary>
/// Tests for the ML risk scoring pipeline:
/// - Feature extraction from Transaction + CustomerProfile
/// - Feature vector correctness
/// - Score contribution mapping
/// </summary>
public class MLFeatureExtractorTests
{
    [Fact]
    public void ExtractFeatures_ShouldExtract26Features()
    {
        // Arrange
        var transaction = CreateTestTransaction();
        var profile = CreateTestProfile();

        // Act
        var features = MLFeatureExtractor.ExtractFeatures(transaction, profile, velocity24h: 3, volume24h: 1500m);

        // Assert
        var array = MLFeatureExtractor.ToArray(features);
        array.Should().HaveCount(26, "ML model expects exactly 26 features");
    }

    [Fact]
    public void ExtractFeatures_ShouldSetAmountCorrectly()
    {
        var transaction = CreateTestTransaction(amount: 5000m);
        var profile = CreateTestProfile();

        var features = MLFeatureExtractor.ExtractFeatures(transaction, profile, 0, 0m);

        features.Amount.Should().Be(5000f);
        features.AmountLog.Should().BeApproximately(MathF.Log(5001f), 0.01f);
    }

    [Fact]
    public void ExtractFeatures_RoundAmount_ShouldBeDetected()
    {
        var transaction = CreateTestTransaction(amount: 3000m);
        var profile = CreateTestProfile();

        var features = MLFeatureExtractor.ExtractFeatures(transaction, profile, 0, 0m);

        features.IsRoundAmount.Should().Be(1f, "3000 is a round amount ≥ 1000");
    }

    [Fact]
    public void ExtractFeatures_NonRoundAmount_ShouldNotBeDetected()
    {
        var transaction = CreateTestTransaction(amount: 3456.78m);
        var profile = CreateTestProfile();

        var features = MLFeatureExtractor.ExtractFeatures(transaction, profile, 0, 0m);

        features.IsRoundAmount.Should().Be(0f);
    }

    [Fact]
    public void ExtractFeatures_NightTime_ShouldBeDetected()
    {
        // 3 AM UTC
        var transaction = CreateTestTransaction();
        transaction.CreatedAt = new DateTime(2026, 2, 25, 3, 30, 0, DateTimeKind.Utc);
        var profile = CreateTestProfile();

        var features = MLFeatureExtractor.ExtractFeatures(transaction, profile, 0, 0m);

        features.IsNightTime.Should().Be(1f, "3:30 AM is in the 2-5 AM window");
        features.HourOfDay.Should().Be(3f);
    }

    [Fact]
    public void ExtractFeatures_DayTime_ShouldNotBeNight()
    {
        var transaction = CreateTestTransaction();
        transaction.CreatedAt = new DateTime(2026, 2, 25, 14, 0, 0, DateTimeKind.Utc);
        var profile = CreateTestProfile();

        var features = MLFeatureExtractor.ExtractFeatures(transaction, profile, 0, 0m);

        features.IsNightTime.Should().Be(0f);
        features.HourOfDay.Should().Be(14f);
    }

    [Fact]
    public void ExtractFeatures_Weekend_ShouldBeDetected()
    {
        // Saturday
        var transaction = CreateTestTransaction();
        transaction.CreatedAt = new DateTime(2026, 2, 28, 12, 0, 0, DateTimeKind.Utc); // Saturday
        var profile = CreateTestProfile();

        var features = MLFeatureExtractor.ExtractFeatures(transaction, profile, 0, 0m);

        features.IsWeekend.Should().Be(1f);
    }

    [Fact]
    public void ExtractFeatures_SendType_ShouldOneHotEncode()
    {
        var transaction = CreateTestTransaction();
        transaction.Type = "SEND";
        var profile = CreateTestProfile();

        var features = MLFeatureExtractor.ExtractFeatures(transaction, profile, 0, 0m);

        features.IsSend.Should().Be(1f);
        features.IsReceive.Should().Be(0f);
        features.IsDeposit.Should().Be(0f);
        features.IsWithdraw.Should().Be(0f);
    }

    [Fact]
    public void ExtractFeatures_CrossBorder_ShouldBeDetected()
    {
        var transaction = CreateTestTransaction();
        transaction.SourceCountry = "NG";
        transaction.DestinationCountry = "US";
        var profile = CreateTestProfile();

        var features = MLFeatureExtractor.ExtractFeatures(transaction, profile, 0, 0m);

        features.IsCrossBorder.Should().Be(1f);
    }

    [Fact]
    public void ExtractFeatures_SameCountry_ShouldNotBeCrossBorder()
    {
        var transaction = CreateTestTransaction();
        transaction.SourceCountry = "NG";
        transaction.DestinationCountry = "NG";
        var profile = CreateTestProfile();

        var features = MLFeatureExtractor.ExtractFeatures(transaction, profile, 0, 0m);

        features.IsCrossBorder.Should().Be(0f);
    }

    [Fact]
    public void ExtractFeatures_HighRiskCountry_ShouldBeDetected()
    {
        var transaction = CreateTestTransaction();
        transaction.SourceCountry = "NG";
        transaction.DestinationCountry = "KP"; // North Korea
        var profile = CreateTestProfile();

        var features = MLFeatureExtractor.ExtractFeatures(transaction, profile, 0, 0m);

        features.IsHighRiskCountry.Should().Be(1f);
    }

    [Fact]
    public void ExtractFeatures_CustomerAge_ShouldComputeDays()
    {
        var transaction = CreateTestTransaction();
        transaction.CreatedAt = new DateTime(2026, 2, 25, 12, 0, 0, DateTimeKind.Utc);
        var profile = CreateTestProfile();
        profile.FirstTransactionAt = new DateTime(2026, 1, 25, 12, 0, 0, DateTimeKind.Utc); // 31 days earlier

        var features = MLFeatureExtractor.ExtractFeatures(transaction, profile, 0, 0m);

        features.CustomerAgeDays.Should().BeApproximately(31f, 0.1f);
    }

    [Fact]
    public void ExtractFeatures_NewCustomerWithNoHistory_ShouldHaveZeroAge()
    {
        var transaction = CreateTestTransaction();
        var profile = CreateTestProfile();
        profile.FirstTransactionAt = null;

        var features = MLFeatureExtractor.ExtractFeatures(transaction, profile, 0, 0m);

        features.CustomerAgeDays.Should().Be(0f);
    }

    [Fact]
    public void ExtractFeatures_AmountDeviation_ShouldCompute()
    {
        var transaction = CreateTestTransaction(amount: 1000m);
        var profile = CreateTestProfile();
        profile.AverageTransactionAmount = 200m; // 5× above average

        var features = MLFeatureExtractor.ExtractFeatures(transaction, profile, 0, 0m);

        features.AmountDeviation.Should().BeApproximately(5f, 0.01f);
    }

    [Fact]
    public void ExtractFeatures_Velocity_ShouldBeSet()
    {
        var transaction = CreateTestTransaction();
        var profile = CreateTestProfile();

        var features = MLFeatureExtractor.ExtractFeatures(transaction, profile, velocity24h: 7, volume24h: 3500m);

        features.Velocity24h.Should().Be(7f);
        features.Volume24h.Should().Be(3500f);
    }

    [Fact]
    public void ExtractFeatures_Label_ShouldBeSetForTraining()
    {
        var transaction = CreateTestTransaction();
        var profile = CreateTestProfile();

        var fraudFeatures = MLFeatureExtractor.ExtractFeatures(transaction, profile, 0, 0m, label: true);
        var legitFeatures = MLFeatureExtractor.ExtractFeatures(transaction, profile, 0, 0m, label: false);

        fraudFeatures.Label.Should().BeTrue();
        legitFeatures.Label.Should().BeFalse();
    }

    [Fact]
    public void ExtractFeatures_FlagAndRejectRates_ShouldCompute()
    {
        var transaction = CreateTestTransaction();
        var profile = CreateTestProfile();
        profile.TotalTransactions = 100;
        profile.FlaggedTransactionCount = 20;
        profile.RejectedTransactionCount = 5;

        var features = MLFeatureExtractor.ExtractFeatures(transaction, profile, 0, 0m);

        features.FlagRate.Should().BeApproximately(0.2f, 0.01f);
        features.RejectRate.Should().BeApproximately(0.05f, 0.01f);
    }

    [Fact]
    public void FeatureNames_ShouldMatch26()
    {
        MLFeatureExtractor.FeatureNames.Should().HaveCount(26);
    }

    [Fact]
    public void ToArray_ShouldReturn26Values()
    {
        var transaction = CreateTestTransaction();
        var profile = CreateTestProfile();
        var features = MLFeatureExtractor.ExtractFeatures(transaction, profile, 0, 0m);

        var array = MLFeatureExtractor.ToArray(features);

        array.Should().HaveCount(26);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static Transaction CreateTestTransaction(decimal amount = 500m) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = "test-tenant",
        ExternalId = "txn-001",
        Type = "SEND",
        Status = "completed",
        Amount = amount,
        SourceCurrency = "NGN",
        DestinationCurrency = "USD",
        SenderId = "customer-001",
        ReceiverId = "customer-002",
        SourceCountry = "NG",
        DestinationCountry = "US",
        CreatedAt = new DateTime(2026, 2, 25, 10, 30, 0, DateTimeKind.Utc)
    };

    private static CustomerProfile CreateTestProfile() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = "test-tenant",
        ExternalId = "customer-001",
        TotalTransactions = 50,
        TotalVolume = 25000m,
        AverageTransactionAmount = 500m,
        MaxTransactionAmount = 2000m,
        KycLevel = KycLevel.Tier2,
        RiskTier = CustomerRiskTier.Standard,
        FlaggedTransactionCount = 3,
        RejectedTransactionCount = 1,
        FirstTransactionAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        LastTransactionAt = new DateTime(2026, 2, 24, 0, 0, 0, DateTimeKind.Utc)
    };
}
