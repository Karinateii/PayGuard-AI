using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using PayGuardAI.Data.Services;
using System.Net;
using System.Text.Json;

namespace PayGuardAI.Tests.Services;

public class FlutterwaveProviderTests
{
    private readonly Mock<HttpMessageHandler> _mockHttpHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<IMemoryCache> _mockCache;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<FlutterwaveProvider>> _mockLogger;
    private readonly FlutterwaveProvider _provider;

    public FlutterwaveProviderTests()
    {
        _mockHttpHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpHandler.Object)
        {
            BaseAddress = new Uri("https://api.flutterwave.com/v3")
        };

        _mockCache = new Mock<IMemoryCache>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<FlutterwaveProvider>>();

        // Setup configuration
        var flutterwaveSection = new Mock<IConfigurationSection>();
        flutterwaveSection.Setup(x => x["SecretKey"]).Returns("test-secret-key");
        flutterwaveSection.Setup(x => x["WebhookSecretHash"]).Returns("test-webhook-hash");

        _mockConfiguration.Setup(x => x.GetSection("Flutterwave")).Returns(flutterwaveSection.Object);

        _provider = new FlutterwaveProvider(
            _httpClient,
            _mockConfiguration.Object,
            _mockLogger.Object,
            _mockCache.Object
        );
    }

    [Fact]
    public void ProviderName_ShouldReturnFlutterwave()
    {
        // Act
        var result = _provider.ProviderName;

        // Assert
        result.Should().Be("Flutterwave");
    }

    [Fact]
    public void IsConfigured_ShouldReturnTrue_WhenKeysArePresent()
    {
        // Act
        var result = _provider.IsConfigured();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_ShouldReturnFalse_WhenKeysAreMissing()
    {
        // Arrange
        var mockConfig = new Mock<IConfiguration>();
        var emptySection = new Mock<IConfigurationSection>();
        emptySection.Setup(x => x["SecretKey"]).Returns(string.Empty);
        emptySection.Setup(x => x["WebhookSecretHash"]).Returns(string.Empty);
        mockConfig.Setup(x => x.GetSection("Flutterwave")).Returns(emptySection.Object);

        var provider = new FlutterwaveProvider(
            _httpClient,
            mockConfig.Object,
            _mockLogger.Object,
            _mockCache.Object
        );

        // Act
        var result = provider.IsConfigured();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task NormalizeWebhookAsync_ShouldMapChargeCompletedEvent()
    {
        // Arrange
        var payload = new
        {
            @event = "charge.completed",
            data = new
            {
                id = 123456,
                tx_ref = "FLW-TEST-001",
                flw_ref = "FLW123456789",
                amount = 100.00,
                currency = "USD",
                status = "successful",
                payment_type = "card",
                customer = new
                {
                    email = "test@example.com",
                    phone_number = "+1234567890"
                },
                card = new
                {
                    country = "US"
                },
                created_at = "2026-02-11T10:00:00Z"
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        // Act
        var result = await _provider.NormalizeWebhookAsync(jsonPayload);

        // Assert
        result.Should().NotBeNull();
        result.TransactionId.Should().Be("FLW123456789");
        result.Provider.Should().Be("Flutterwave");
        result.CustomerId.Should().Be("test@example.com");
        result.SourceAmount.Should().Be(100m);
        result.SourceCurrency.Should().Be("USD");
        result.SourceCountry.Should().Be("US");
        result.Status.Should().Be("COMPLETED");
    }

    [Fact]
    public async Task NormalizeWebhookAsync_ShouldMapTransferCompletedEvent()
    {
        // Arrange
        var payload = new
        {
            @event = "transfer.completed",
            data = new
            {
                id = 789012,
                reference = "FLW-TRANSFER-001",
                amount = 50000,
                currency = "NGN",
                status = "successful",
                created_at = "2026-02-11T11:00:00Z"
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        // Act
        var result = await _provider.NormalizeWebhookAsync(jsonPayload);

        // Assert
        result.TransactionId.Should().Be("FLW-TRANSFER-001");
        result.SourceAmount.Should().Be(50000m);
        result.SourceCurrency.Should().Be("NGN");
        result.Status.Should().Be("COMPLETED");
    }

    [Theory]
    [InlineData("successful", "COMPLETED")]
    [InlineData("failed", "FAILED")]
    [InlineData("pending", "PENDING")]
    [InlineData("unknown", "PENDING")]
    public async Task NormalizeWebhookAsync_ShouldMapStatusCorrectly(string flutterwaveStatus, string expectedStatus)
    {
        // Arrange
        var payload = new
        {
            @event = "charge.completed",
            data = new
            {
                id = 123456,
                tx_ref = "FLW-STATUS-TEST",
                flw_ref = "FLW-STATUS",
                amount = 100,
                currency = "USD",
                status = flutterwaveStatus,
                payment_type = "card",
                customer = new { email = "test@example.com" },
                created_at = "2026-02-11T10:00:00Z"
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        // Act
        var result = await _provider.NormalizeWebhookAsync(jsonPayload);

        // Assert
        result.Status.Should().Be(expectedStatus);
    }

    [Theory]
    [InlineData("card", "US")]
    [InlineData("mobile_money_uganda", "UG")]
    [InlineData("mobile_money_ghana", "GH")]
    [InlineData("mobile_money_rwanda", "RW")]
    [InlineData("mobile_money_zambia", "ZM")]
    [InlineData("mpesa", "KE")]
    [InlineData("bank_transfer", "NG")]
    [InlineData("unknown_type", "NG")]
    public async Task NormalizeWebhookAsync_ShouldInferCountryFromPaymentType(string paymentType, string expectedCountry)
    {
        // Arrange
        var payload = new
        {
            @event = "charge.completed",
            data = new
            {
                id = 123456,
                tx_ref = "FLW-COUNTRY-TEST",
                flw_ref = "FLW-COUNTRY",
                amount = 100,
                currency = "USD",
                status = "successful",
                payment_type = paymentType,
                customer = new { email = "test@example.com" },
                created_at = "2026-02-11T10:00:00Z"
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        // Act
        var result = await _provider.NormalizeWebhookAsync(jsonPayload);

        // Assert
        result.DestinationCountry.Should().Be(expectedCountry);
    }

    [Fact]
    public async Task NormalizeWebhookAsync_ShouldUseCardCountryWhenAvailable()
    {
        // Arrange
        var payload = new
        {
            @event = "charge.completed",
            data = new
            {
                id = 123456,
                tx_ref = "FLW-CARD-COUNTRY",
                flw_ref = "FLW-CARD",
                amount = 100,
                currency = "USD",
                status = "successful",
                payment_type = "card",
                customer = new { email = "test@example.com" },
                card = new { country = "GB" },
                created_at = "2026-02-11T10:00:00Z"
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        // Act
        var result = await _provider.NormalizeWebhookAsync(jsonPayload);

        // Assert
        result.SourceCountry.Should().Be("GB");
    }

    [Fact]
    public void VerifyWebhookSignature_ShouldReturnTrue_WhenSignatureIsValid()
    {
        // Arrange
        var payload = "{\"event\":\"charge.completed\"}";
        var expectedHash = "6c40d60b6a41c10bb8de577a39e2a0d6e86f0aa0a1e4e8f0b3d5b33cc6c37e7d"; // HMAC-SHA256 hash

        // Act
        var result = _provider.VerifyWebhookSignature(payload, expectedHash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyWebhookSignature_ShouldReturnFalse_WhenSignatureIsInvalid()
    {
        // Arrange
        var payload = "{\"event\":\"charge.completed\"}";
        var signature = "invalid-hash";

        // Act
        var result = _provider.VerifyWebhookSignature(payload, signature);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyWebhookSignature_ShouldReturnFalse_WhenSignatureIsEmpty()
    {
        // Arrange
        var payload = "{\"event\":\"charge.completed\"}";
        var signature = "";

        // Act
        var result = _provider.VerifyWebhookSignature(payload, signature);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetExchangeRateAsync_ShouldReturnCachedRate_WhenAvailable()
    {
        // Arrange
        object cachedRate = 1500m;
        _mockCache.Setup(x => x.TryGetValue("flutterwave_rate_USD_NGN", out cachedRate))
            .Returns(true);

        // Act
        var result = await _provider.GetExchangeRateAsync("USD", "NGN", 100m);

        // Assert
        result.Should().Be(1500m);
        _mockHttpHandler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task GetExchangeRateAsync_ShouldFetchAndCacheRate_WhenNotCached()
    {
        // Arrange
        object? cachedRate = null;
        _mockCache.Setup(x => x.TryGetValue("flutterwave_rate_USD_NGN", out cachedRate))
            .Returns(false);

        var mockCacheEntry = new Mock<ICacheEntry>();
        _mockCache.Setup(x => x.CreateEntry(It.IsAny<object>()))
            .Returns(mockCacheEntry.Object);

        var responseContent = new
        {
            status = "success",
            data = new { rate = 1500 }
        };

        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(responseContent))
        };

        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(response);

        // Act
        var result = await _provider.GetExchangeRateAsync("USD", "NGN", 100m);

        // Assert
        result.Should().Be(1500m);
        _mockCache.Verify(x => x.CreateEntry("flutterwave_rate_USD_NGN"), Times.Once);
    }

    [Fact]
    public async Task NormalizeWebhookAsync_ShouldThrowException_WhenPayloadIsInvalid()
    {
        // Arrange
        var invalidPayload = "{invalid json}";

        // Act
        Func<Task> act = async () => await _provider.NormalizeWebhookAsync(invalidPayload);

        // Assert
        await act.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task NormalizeWebhookAsync_ShouldHandleMissingCustomerEmail()
    {
        // Arrange
        var payload = new
        {
            @event = "charge.completed",
            data = new
            {
                id = 123456,
                tx_ref = "FLW-NO-EMAIL",
                flw_ref = "FLW-NO-EMAIL-REF",
                amount = 100,
                currency = "USD",
                status = "successful",
                payment_type = "card",
                customer = new { phone_number = "+1234567890" },
                created_at = "2026-02-11T10:00:00Z"
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        // Act
        var result = await _provider.NormalizeWebhookAsync(jsonPayload);

        // Assert
        result.CustomerId.Should().Be("+1234567890");
    }
}
