using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PayGuardAI.Core.Services;
using PayGuardAI.Data.Services;

namespace PayGuardAI.Tests.Services;

public class PaymentProviderFactoryTests
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<PaymentProviderFactory>> _mockLogger;
    private readonly Mock<IPaymentProvider> _mockAfriexProvider;
    private readonly Mock<IPaymentProvider> _mockFlutterwaveProvider;

    public PaymentProviderFactoryTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<PaymentProviderFactory>>();

        // Setup mock providers with lowercase names to match implementation
        _mockAfriexProvider = new Mock<IPaymentProvider>();
        _mockAfriexProvider.Setup(x => x.ProviderName).Returns("afriex");
        _mockAfriexProvider.Setup(x => x.IsConfigured()).Returns(true);

        _mockFlutterwaveProvider = new Mock<IPaymentProvider>();
        _mockFlutterwaveProvider.Setup(x => x.ProviderName).Returns("flutterwave");
        _mockFlutterwaveProvider.Setup(x => x.IsConfigured()).Returns(true);
    }

    [Fact]
    public void Constructor_ShouldRegisterAfriexProvider_Always()
    {
        // Arrange
        _mockServiceProvider.Setup(x => x.GetService(typeof(AfriexProvider)))
            .Returns(_mockAfriexProvider.Object);

        var featureFlagsSection = new Mock<IConfigurationSection>();
        featureFlagsSection.Setup(x => x["FlutterwaveEnabled"]).Returns("false");
        _mockConfiguration.Setup(x => x.GetSection("FeatureFlags")).Returns(featureFlagsSection.Object);

        // Act
        var factory = new PaymentProviderFactory(
            _mockServiceProvider.Object,
            _mockConfiguration.Object,
            _mockLogger.Object
        );

        var allProviders = factory.GetAllProviders();

        // Assert
        allProviders.Should().ContainSingle();
        allProviders.First().ProviderName.Should().Be("afriex");
    }

    [Fact]
    public void Constructor_ShouldRegisterFlutterwaveProvider_WhenFeatureFlagEnabled()
    {
        // Arrange
        _mockServiceProvider.Setup(x => x.GetService(typeof(AfriexProvider)))
            .Returns(_mockAfriexProvider.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(FlutterwaveProvider)))
            .Returns(_mockFlutterwaveProvider.Object);

        var featureFlagsSection = new Mock<IConfigurationSection>();
        featureFlagsSection.Setup(x => x["FlutterwaveEnabled"]).Returns("true");
        _mockConfiguration.Setup(x => x.GetSection("FeatureFlags")).Returns(featureFlagsSection.Object);

        // Act
        var factory = new PaymentProviderFactory(
            _mockServiceProvider.Object,
            _mockConfiguration.Object,
            _mockLogger.Object
        );

        var allProviders = factory.GetAllProviders();

        // Assert
        allProviders.Should().HaveCount(2);
        allProviders.Select(p => p.ProviderName).Should().Contain(new[] { "afriex", "flutterwave" });
    }

    [Fact]
    public void Constructor_ShouldNotRegisterFlutterwaveProvider_WhenFeatureFlagDisabled()
    {
        // Arrange
        _mockServiceProvider.Setup(x => x.GetService(typeof(AfriexProvider)))
            .Returns(_mockAfriexProvider.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(FlutterwaveProvider)))
            .Returns(_mockFlutterwaveProvider.Object);

        var featureFlagsSection = new Mock<IConfigurationSection>();
        featureFlagsSection.Setup(x => x["FlutterwaveEnabled"]).Returns("false");
        _mockConfiguration.Setup(x => x.GetSection("FeatureFlags")).Returns(featureFlagsSection.Object);

        // Act
        var factory = new PaymentProviderFactory(
            _mockServiceProvider.Object,
            _mockConfiguration.Object,
            _mockLogger.Object
        );

        var allProviders = factory.GetAllProviders();

        // Assert
        allProviders.Should().ContainSingle();
        allProviders.Select(p => p.ProviderName).Should().NotContain("Flutterwave");
    }

    [Fact]
    public void Constructor_ShouldNotRegisterFlutterwaveProvider_WhenNotConfigured()
    {
        // Arrange
        var unconfiguredFlutterwaveProvider = new Mock<IPaymentProvider>();
        unconfiguredFlutterwaveProvider.Setup(x => x.ProviderName).Returns("Flutterwave");
        unconfiguredFlutterwaveProvider.Setup(x => x.IsConfigured()).Returns(false);

        _mockServiceProvider.Setup(x => x.GetService(typeof(AfriexProvider)))
            .Returns(_mockAfriexProvider.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(FlutterwaveProvider)))
            .Returns(unconfiguredFlutterwaveProvider.Object);

        var featureFlagsSection = new Mock<IConfigurationSection>();
        featureFlagsSection.Setup(x => x["FlutterwaveEnabled"]).Returns("true");
        _mockConfiguration.Setup(x => x.GetSection("FeatureFlags")).Returns(featureFlagsSection.Object);

        // Act
        var factory = new PaymentProviderFactory(
            _mockServiceProvider.Object,
            _mockConfiguration.Object,
            _mockLogger.Object
        );

        var allProviders = factory.GetAllProviders();

        // Assert
        allProviders.Should().ContainSingle();
        allProviders.Select(p => p.ProviderName).Should().NotContain("Flutterwave");
    }

    [Fact]
    public void GetProvider_ShouldReturnFlutterwaveProvider_WhenEnabled()
    {
        // Arrange
        _mockServiceProvider.Setup(x => x.GetService(typeof(AfriexProvider)))
            .Returns(_mockAfriexProvider.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(FlutterwaveProvider)))
            .Returns(_mockFlutterwaveProvider.Object);

        var featureFlagsSection = new Mock<IConfigurationSection>();
        featureFlagsSection.Setup(x => x["FlutterwaveEnabled"]).Returns("true");
        _mockConfiguration.Setup(x => x.GetSection("FeatureFlags")).Returns(featureFlagsSection.Object);

        var factory = new PaymentProviderFactory(
            _mockServiceProvider.Object,
            _mockConfiguration.Object,
            _mockLogger.Object
        );

        // Act
        var provider = factory.GetProvider();

        // Assert
        provider.Should().NotBeNull();
        provider.ProviderName.Should().Be("Flutterwave"); // Priority: Flutterwave > Afriex
    }

    [Fact]
    public void GetProvider_ShouldReturnAfriexProvider_WhenFlutterwaveDisabled()
    {
        // Arrange
        _mockServiceProvider.Setup(x => x.GetService(typeof(AfriexProvider)))
            .Returns(_mockAfriexProvider.Object);

        var featureFlagsSection = new Mock<IConfigurationSection>();
        featureFlagsSection.Setup(x => x["FlutterwaveEnabled"]).Returns("false");
        _mockConfiguration.Setup(x => x.GetSection("FeatureFlags")).Returns(featureFlagsSection.Object);

        var factory = new PaymentProviderFactory(
            _mockServiceProvider.Object,
            _mockConfiguration.Object,
            _mockLogger.Object
        );

        // Act
        var provider = factory.GetProvider();

        // Assert
        provider.Should().NotBeNull();
        provider.ProviderName.Should().Be("Afriex");
    }

    [Fact]
    public void GetProviderByName_ShouldReturnSpecificProvider_WhenExists()
    {
        // Arrange
        _mockServiceProvider.Setup(x => x.GetService(typeof(AfriexProvider)))
            .Returns(_mockAfriexProvider.Object);

        var featureFlagsSection = new Mock<IConfigurationSection>();
        featureFlagsSection.Setup(x => x["FlutterwaveEnabled"]).Returns("false");
        _mockConfiguration.Setup(x => x.GetSection("FeatureFlags")).Returns(featureFlagsSection.Object);

        var factory = new PaymentProviderFactory(
            _mockServiceProvider.Object,
            _mockConfiguration.Object,
            _mockLogger.Object
        );

        // Act
        var provider = factory.GetProviderByName("afriex"); // Case-insensitive

        // Assert
        provider.Should().NotBeNull();
        provider!.ProviderName.Should().Be("Afriex");
    }

    [Fact]
    public void GetProviderByName_ShouldThrowException_WhenProviderNotFound()
    {
        // Arrange
        _mockServiceProvider.Setup(x => x.GetService(typeof(AfriexProvider)))
            .Returns(_mockAfriexProvider.Object);

        var featureFlagsSection = new Mock<IConfigurationSection>();
        featureFlagsSection.Setup(x => x["FlutterwaveEnabled"]).Returns("false");
        _mockConfiguration.Setup(x => x.GetSection("FeatureFlags")).Returns(featureFlagsSection.Object);

        var factory = new PaymentProviderFactory(
            _mockServiceProvider.Object,
            _mockConfiguration.Object,
            _mockLogger.Object
        );

        // Act
        Action act = () => factory.GetProviderByName("wise");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Payment provider 'wise' is not registered*");
    }

    [Fact]
    public void GetAllProviders_ShouldReturnAllRegisteredProviders()
    {
        // Arrange
        _mockServiceProvider.Setup(x => x.GetService(typeof(AfriexProvider)))
            .Returns(_mockAfriexProvider.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(FlutterwaveProvider)))
            .Returns(_mockFlutterwaveProvider.Object);

        var featureFlagsSection = new Mock<IConfigurationSection>();
        featureFlagsSection.Setup(x => x["FlutterwaveEnabled"]).Returns("true");
        _mockConfiguration.Setup(x => x.GetSection("FeatureFlags")).Returns(featureFlagsSection.Object);

        var factory = new PaymentProviderFactory(
            _mockServiceProvider.Object,
            _mockConfiguration.Object,
            _mockLogger.Object
        );

        // Act
        var providers = factory.GetAllProviders();

        // Assert
        providers.Should().HaveCount(2);
        providers.Select(p => p.ProviderName).Should().BeEquivalentTo("Afriex", "Flutterwave");
    }

    [Fact]
    public void GetProvider_ShouldThrowException_WhenNoProvidersAvailable()
    {
        // Arrange
        _mockServiceProvider.Setup(x => x.GetService(typeof(AfriexProvider)))
            .Returns(null); // No providers available

        var featureFlagsSection = new Mock<IConfigurationSection>();
        featureFlagsSection.Setup(x => x["FlutterwaveEnabled"]).Returns("false");
        _mockConfiguration.Setup(x => x.GetSection("FeatureFlags")).Returns(featureFlagsSection.Object);

        var factory = new PaymentProviderFactory(
            _mockServiceProvider.Object,
            _mockConfiguration.Object,
            _mockLogger.Object
        );

        // Act
        Action act = () => factory.GetProvider();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No payment providers are configured*");
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("FALSE", false)]
    [InlineData("invalid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Constructor_ShouldParseFeatureFlag_Correctly(string? flagValue, bool shouldRegisterFlutterwave)
    {
        // Arrange
        _mockServiceProvider.Setup(x => x.GetService(typeof(AfriexProvider)))
            .Returns(_mockAfriexProvider.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(FlutterwaveProvider)))
            .Returns(_mockFlutterwaveProvider.Object);

        var featureFlagsSection = new Mock<IConfigurationSection>();
        featureFlagsSection.Setup(x => x["FlutterwaveEnabled"]).Returns(flagValue);
        _mockConfiguration.Setup(x => x.GetSection("FeatureFlags")).Returns(featureFlagsSection.Object);

        // Act
        var factory = new PaymentProviderFactory(
            _mockServiceProvider.Object,
            _mockConfiguration.Object,
            _mockLogger.Object
        );

        var allProviders = factory.GetAllProviders();

        // Assert
        if (shouldRegisterFlutterwave)
        {
            allProviders.Select(p => p.ProviderName).Should().Contain("Flutterwave");
        }
        else
        {
            allProviders.Select(p => p.ProviderName).Should().NotContain("Flutterwave");
        }
    }
}
