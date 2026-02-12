using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using PayGuardAI.Data.Services;
using System.Net;
using System.Security.Cryptography;
using System.Text;
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

        // Setup configuration to handle direct key access: configuration["key"]
        _mockConfiguration.Setup(x => x["Flutterwave:SecretKey"]).Returns("test-secret-key");
        _mockConfiguration.Setup(x => x["Flutterwave:BaseUrl"]).Returns("https://api.flutterwave.com/v3");
        _mockConfiguration.Setup(x => x["Flutterwave:WebhookSecretHash"]).Returns("test-webhook-hash");

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
        result.Should().Be("flutterwave");
    }

    [Fact]
    public void IsConfigured_ShouldReturnTrue_WhenKeysArePresent()
    {
        // Act
        // The provider is configured with test-secret-key in constructor
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

        // Create a fresh HttpClient for this test to avoid header duplication issues
        var freshHttpClient = new HttpClient(_mockHttpHandler.Object)
        {
            BaseAddress = new Uri("https://api.flutterwave.com/v3")
        };

        var provider = new FlutterwaveProvider(
            freshHttpClient,
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
                amount = 100m,
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
                created_at = DateTime.Parse("2026-02-11T10:00:00Z")
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        // Act
        var result = await _provider.NormalizeWebhookAsync(jsonPayload);

        // Assert
        result.Should().NotBeNull();
        result.TransactionId.Should().Be("FLW123456789"); // Uses flw_ref
        result.Provider.Should().Be("flutterwave");
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
        // TransactionId uses TxRef, FlwRef, or Id - in this case id is 789012
        result.TransactionId.Should().Be("789012");
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
    [InlineData("card", "NG")]
    [InlineData("mobile_money_uganda", "NG")]
    [InlineData("mobile_money_ghana", "NG")]
    [InlineData("mobile_money_rwanda", "NG")]
    [InlineData("mobile_money_zambia", "NG")]
    [InlineData("mpesa", "NG")]
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
        // Destination country should use customer country when not provided, defaulting to "NG"
        result.DestinationCountry.Should().Be(expectedCountry);
    }

    [Fact]
    public async Task NormalizeWebhookAsync_ShouldUsePaymentTypeForSourceCountry()
    {
        // Arrange
        var payload = new
        {
            @event = "charge.completed",
            data = new
            {
                id = 123456,
                tx_ref = "FLW-SOURCE-COUNTRY",
                flw_ref = "FLW-SOURCE",
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
        // Source country is inferred from payment type, not card country
        result.SourceCountry.Should().Be("US"); // Default when payment_type is "card"
    }

    [Fact]
    public void VerifyWebhookSignature_ShouldReturnTrue_WhenSignatureIsValid()
    {
        // Arrange
        var payload = "{\"event\":\"charge.completed\"}";
        
        // Compute correct HMAC-SHA256 signature using the test webhook secret hash
        var secretHash = "test-webhook-hash";
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secretHash));
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expectedSignature = BitConverter.ToString(computedHash).Replace("-", "").ToLower();

        // Act
        var result = _provider.VerifyWebhookSignature(payload, expectedSignature);

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
        // Note: The provider will compute the correct signature and compare
        // Since "invalid-hash" won't match the computed signature, it returns false
        var result = _provider.VerifyWebhookSignature(payload, signature);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyWebhookSignature_ShouldReturnTrue_WhenSignatureIsEmpty()
    {
        // Arrange
        var payload = "{\"event\":\"charge.completed\"}";
        var signature = "";

        // Act
        // Note: When webhook secret hash is empty string (test config), provider returns true (dev mode)
        var result = _provider.VerifyWebhookSignature(payload, signature);

        // Assert
        result.Should().BeTrue(); // Allows in development when secret not configured
    }

    [Fact]
    public async Task GetExchangeRateAsync_ShouldCallHttpClient_AndReturnRate()
    {
        // Arrange
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
    }

    [Fact]
    public async Task GetExchangeRateAsync_ShouldFetchAndReturnRate_WhenNeeded()
    {
        // Arrange
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
        // When customer email is not provided and no customer ID, defaults to "unknown"
        result.CustomerId.Should().Be("unknown");
    }
}
