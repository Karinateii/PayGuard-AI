using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PayGuardAI.Core.Services;
using PayGuardAI.Data.Services;
using System.Text;
using System.Text.Json;

namespace PayGuardAI.Tests.Services;

public class AfriexProviderTests
{
    private readonly Mock<IAfriexApiService> _mockAfriexService;
    private readonly Mock<IWebhookSignatureService> _mockSignatureService;
    private readonly Mock<ILogger<AfriexProvider>> _mockLogger;
    private readonly AfriexProvider _provider;

    public AfriexProviderTests()
    {
        _mockAfriexService = new Mock<IAfriexApiService>();
        _mockSignatureService = new Mock<IWebhookSignatureService>();
        _mockLogger = new Mock<ILogger<AfriexProvider>>();

        _provider = new AfriexProvider(
            _mockAfriexService.Object,
            _mockSignatureService.Object,
            _mockLogger.Object
        );
    }

    [Fact]
    public void ProviderName_ShouldReturnAfriex()
    {
        // Act
        var result = _provider.ProviderName;

        // Assert
        result.Should().Be("Afriex");
    }

    [Fact]
    public void IsConfigured_ShouldReturnTrue()
    {
        // Act
        var result = _provider.IsConfigured();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task NormalizeWebhookAsync_ShouldMapAfriexPayloadCorrectly()
    {
        // Arrange
        var payload = new
        {
            @event = "TRANSACTION.CREATED",
            data = new
            {
                transactionId = "AFX-TEST-001",
                customerId = "cust-001",
                sourceAmount = "100",
                sourceCurrency = "USD",
                destinationAmount = "150000",
                destinationCurrency = "NGN",
                sourceCountry = "US",
                destinationCountry = "NG",
                status = "PENDING",
                createdAt = "2026-02-11T10:00:00Z"
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        // Act
        var result = await _provider.NormalizeWebhookAsync(jsonPayload);

        // Assert
        result.Should().NotBeNull();
        result.TransactionId.Should().Be("AFX-TEST-001");
        result.Provider.Should().Be("Afriex");
        result.CustomerId.Should().Be("cust-001");
        result.SourceAmount.Should().Be(100m);
        result.SourceCurrency.Should().Be("USD");
        result.DestinationAmount.Should().Be(150000m);
        result.DestinationCurrency.Should().Be("NGN");
        result.SourceCountry.Should().Be("US");
        result.DestinationCountry.Should().Be("NG");
        result.Status.Should().Be("PENDING");
        result.CreatedAt.Should().Be(DateTime.Parse("2026-02-11T10:00:00Z"));
    }

    [Fact]
    public async Task NormalizeWebhookAsync_ShouldHandleTransactionUpdatedEvent()
    {
        // Arrange
        var payload = new
        {
            @event = "TRANSACTION.UPDATED",
            data = new
            {
                transactionId = "AFX-TEST-002",
                customerId = "cust-002",
                sourceAmount = "500",
                sourceCurrency = "USD",
                destinationAmount = "750000",
                destinationCurrency = "NGN",
                sourceCountry = "US",
                destinationCountry = "NG",
                status = "COMPLETED",
                createdAt = "2026-02-11T11:00:00Z"
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        // Act
        var result = await _provider.NormalizeWebhookAsync(jsonPayload);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("COMPLETED");
    }

    [Theory]
    [InlineData("PENDING", "PENDING")]
    [InlineData("COMPLETED", "COMPLETED")]
    [InlineData("FAILED", "FAILED")]
    [InlineData("PROCESSING", "PENDING")]
    [InlineData("CANCELLED", "FAILED")]
    [InlineData("UNKNOWN", "PENDING")]
    public async Task NormalizeWebhookAsync_ShouldMapStatusCorrectly(string afriexStatus, string expectedStatus)
    {
        // Arrange
        var payload = new
        {
            @event = "TRANSACTION.CREATED",
            data = new
            {
                transactionId = "AFX-TEST-STATUS",
                customerId = "cust-status",
                sourceAmount = "100",
                sourceCurrency = "USD",
                destinationAmount = "150000",
                destinationCurrency = "NGN",
                sourceCountry = "US",
                destinationCountry = "NG",
                status = afriexStatus,
                createdAt = "2026-02-11T10:00:00Z"
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        // Act
        var result = await _provider.NormalizeWebhookAsync(jsonPayload);

        // Assert
        result.Status.Should().Be(expectedStatus);
    }

    [Fact]
    public void VerifyWebhookSignature_ShouldReturnTrue_WhenSignatureIsValid()
    {
        // Arrange
        var payload = "{\"event\":\"TRANSACTION.CREATED\"}";
        var signature = "valid-signature";

        _mockSignatureService
            .Setup(x => x.VerifySignature(payload, signature))
            .Returns(true);

        // Act
        var result = _provider.VerifyWebhookSignature(payload, signature);

        // Assert
        result.Should().BeTrue();
        _mockSignatureService.Verify(x => x.VerifySignature(payload, signature), Times.Once);
    }

    [Fact]
    public void VerifyWebhookSignature_ShouldReturnFalse_WhenSignatureIsInvalid()
    {
        // Arrange
        var payload = "{\"event\":\"TRANSACTION.CREATED\"}";
        var signature = "invalid-signature";

        _mockSignatureService
            .Setup(x => x.VerifySignature(payload, signature))
            .Returns(false);

        // Act
        var result = _provider.VerifyWebhookSignature(payload, signature);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyWebhookSignature_ShouldReturnFalse_WhenSignatureIsEmpty()
    {
        // Arrange
        var payload = "{\"event\":\"TRANSACTION.CREATED\"}";
        var signature = "";

        // Act
        var result = _provider.VerifyWebhookSignature(payload, signature);

        // Assert
        result.Should().BeFalse();
        _mockSignatureService.Verify(x => x.VerifySignature(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetExchangeRateAsync_ShouldCallAfriexApiService()
    {
        // Arrange
        var exchangeRateResponse = new PayGuardAI.Data.Services.ExchangeRateResponse
        {
            Rate = 1500m,
            From = "USD",
            To = "NGN",
            SourceAmount = 100m,
            DestinationAmount = 150000m
        };

        _mockAfriexService
            .Setup(x => x.GetExchangeRateAsync("USD", "NGN", 100m))
            .ReturnsAsync(exchangeRateResponse);

        // Act
        var result = await _provider.GetExchangeRateAsync("USD", "NGN", 100m);

        // Assert
        result.Should().Be(1500m);
        _mockAfriexService.Verify(x => x.GetExchangeRateAsync("USD", "NGN", 100m), Times.Once);
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
    public async Task NormalizeWebhookAsync_ShouldHandleDecimalAmounts()
    {
        // Arrange
        var payload = new
        {
            @event = "TRANSACTION.CREATED",
            data = new
            {
                transactionId = "AFX-DECIMAL-TEST",
                customerId = "cust-decimal",
                sourceAmount = "123.45",
                sourceCurrency = "USD",
                destinationAmount = "185175",
                destinationCurrency = "NGN",
                sourceCountry = "US",
                destinationCountry = "NG",
                status = "PENDING",
                createdAt = "2026-02-11T10:00:00Z"
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        // Act
        var result = await _provider.NormalizeWebhookAsync(jsonPayload);

        // Assert
        result.SourceAmount.Should().Be(123.45m);
        result.DestinationAmount.Should().Be(185175m);
    }
}
