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

public class WiseProviderTests
{
    private readonly Mock<HttpMessageHandler> _mockHttpHandler;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<WiseProvider>> _mockLogger;
    private readonly WiseProvider _provider;

    // RSA key pair for signature verification tests
    private readonly RSA _rsa;
    private readonly string _publicKeyPem;

    public WiseProviderTests()
    {
        _mockHttpHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpHandler.Object)
        {
            BaseAddress = new Uri("https://api.transferwise.com")
        };

        _cache = new MemoryCache(new MemoryCacheOptions());
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<WiseProvider>>();

        // Generate an RSA key pair for signature tests
        _rsa = RSA.Create(2048);
        _publicKeyPem = ExportPublicKeyPem(_rsa);

        // Setup configuration
        _mockConfiguration.Setup(x => x["Wise:ApiToken"]).Returns("test-api-token");
        _mockConfiguration.Setup(x => x["Wise:BaseUrl"]).Returns("https://api.transferwise.com");
        _mockConfiguration.Setup(x => x["Wise:WebhookPublicKey"]).Returns(_publicKeyPem);
        _mockConfiguration.Setup(x => x["Wise:ProfileId"]).Returns("12345678");

        _provider = new WiseProvider(
            _httpClient,
            _mockConfiguration.Object,
            _mockLogger.Object,
            _cache
        );
    }

    [Fact]
    public void ProviderName_ShouldReturnWise()
    {
        _provider.ProviderName.Should().Be("wise");
    }

    [Fact]
    public void IsConfigured_ShouldReturnTrue_WhenApiTokenIsPresent()
    {
        _provider.IsConfigured().Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_ShouldReturnFalse_WhenApiTokenIsMissing()
    {
        var mockConfig = new Mock<IConfiguration>();
        // Return empty/null for all Wise config keys
        var freshHttpClient = new HttpClient(_mockHttpHandler.Object)
        {
            BaseAddress = new Uri("https://api.transferwise.com")
        };
        var freshCache = new MemoryCache(new MemoryCacheOptions());

        var provider = new WiseProvider(
            freshHttpClient,
            mockConfig.Object,
            _mockLogger.Object,
            freshCache
        );

        provider.IsConfigured().Should().BeFalse();
    }

    [Fact]
    public async Task NormalizeWebhookAsync_ShouldMapTransferStateChange()
    {
        // Arrange - Wise transfer state change webhook
        var payload = new
        {
            event_type = "transfers#state-change",
            schema_version = "2.0.0",
            sent_at = "2026-03-15T10:30:00Z",
            data = new
            {
                resource = new
                {
                    id = 16521632L,
                    profile_id = 12345678L,
                    account_id = 8765432L,
                    type = "transfer",
                    source_currency = "USD",
                    source_amount = 500.00m,
                    target_currency = "NGN",
                    target_amount = 750000.00m
                },
                current_state = "outgoing_payment_sent",
                previous_state = "processing",
                occurred_at = "2026-03-15T10:30:00Z",
                source_currency = "USD",
                source_amount = 500.00m,
                target_currency = "NGN",
                target_amount = 750000.00m,
                customer_email = "sender@example.com"
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        // Act
        var result = await _provider.NormalizeWebhookAsync(jsonPayload);

        // Assert
        result.Should().NotBeNull();
        result.TransactionId.Should().Be("16521632");
        result.Provider.Should().Be("wise");
        result.CustomerId.Should().Be("12345678");
        result.CustomerEmail.Should().Be("sender@example.com");
        result.SourceCurrency.Should().Be("USD");
        result.SourceAmount.Should().Be(500.00m);
        result.DestinationCurrency.Should().Be("NGN");
        result.DestinationAmount.Should().Be(750000.00m);
        result.SourceCountry.Should().Be("US");
        result.DestinationCountry.Should().Be("NG");
        result.Status.Should().Be("COMPLETED");
        result.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task NormalizeWebhookAsync_ShouldMapPendingTransfer()
    {
        var payload = new
        {
            event_type = "transfers#state-change",
            data = new
            {
                resource = new
                {
                    id = 99887766L,
                    profile_id = 12345678L,
                    source_currency = "GBP",
                    source_amount = 200.00m,
                    target_currency = "KES",
                    target_amount = 36000.00m
                },
                current_state = "incoming_payment_waiting",
                occurred_at = "2026-03-15T09:00:00Z",
                source_currency = "GBP",
                source_amount = 200.00m,
                target_currency = "KES",
                target_amount = 36000.00m
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        var result = await _provider.NormalizeWebhookAsync(jsonPayload);

        result.TransactionId.Should().Be("99887766");
        result.SourceCurrency.Should().Be("GBP");
        result.DestinationCurrency.Should().Be("KES");
        result.SourceCountry.Should().Be("GB");
        result.DestinationCountry.Should().Be("KE");
        result.Status.Should().Be("PENDING");
        result.CompletedAt.Should().BeNull();
    }

    [Theory]
    [InlineData("outgoing_payment_sent", "COMPLETED")]
    [InlineData("funds_converted", "PROCESSING")]
    [InlineData("processing", "PROCESSING")]
    [InlineData("incoming_payment_waiting", "PENDING")]
    [InlineData("waiting_recipient_input_to_proceed", "PENDING")]
    [InlineData("bounced_back", "FAILED")]
    [InlineData("funds_refunded", "FAILED")]
    [InlineData("cancelled", "CANCELLED")]
    [InlineData("charged_back", "CANCELLED")]
    [InlineData("unknown_state", "PENDING")]
    public void NormalizeStatus_ShouldMapWiseStatesCorrectly(string wiseState, string expectedStatus)
    {
        var result = WiseProvider.NormalizeStatus(wiseState);
        result.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task NormalizeWebhookAsync_ShouldHandleBouncedTransfer()
    {
        var payload = new
        {
            event_type = "transfers#state-change",
            data = new
            {
                resource = new
                {
                    id = 55443322L,
                    profile_id = 12345678L,
                    source_currency = "EUR",
                    source_amount = 1000.00m,
                    target_currency = "GHS",
                    target_amount = 16500.00m
                },
                current_state = "bounced_back",
                previous_state = "outgoing_payment_sent",
                occurred_at = "2026-03-15T14:00:00Z"
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        var result = await _provider.NormalizeWebhookAsync(jsonPayload);

        result.Status.Should().Be("FAILED");
        result.SourceCountry.Should().Be("DE"); // EUR -> DE
        result.DestinationCountry.Should().Be("GH"); // GHS -> GH
        result.CompletedAt.Should().BeNull(); // bounced_back is not completed
        result.Metadata.Should().ContainKey("previousState")
            .WhoseValue.Should().Be("outgoing_payment_sent");
    }

    [Fact]
    public async Task NormalizeWebhookAsync_ShouldThrow_WhenPayloadIsInvalid()
    {
        var invalidPayload = "{invalid json}";

        Func<Task> act = async () => await _provider.NormalizeWebhookAsync(invalidPayload);

        await act.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task NormalizeWebhookAsync_ShouldThrow_WhenDataIsMissing()
    {
        var payload = new { event_type = "transfers#state-change" };
        var jsonPayload = JsonSerializer.Serialize(payload);

        Func<Task> act = async () => await _provider.NormalizeWebhookAsync(jsonPayload);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Invalid Wise webhook payload*");
    }

    [Fact]
    public async Task NormalizeWebhookAsync_ShouldIncludeMetadata()
    {
        var payload = new
        {
            event_type = "transfers#state-change",
            data = new
            {
                resource = new { id = 11223344L, profile_id = 12345678L },
                current_state = "processing",
                previous_state = "incoming_payment_waiting",
                source_currency = "USD",
                source_amount = 100m,
                target_currency = "NGN",
                target_amount = 150000m
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        var result = await _provider.NormalizeWebhookAsync(jsonPayload);

        result.Metadata.Should().ContainKey("originalEvent")
            .WhoseValue.Should().Be("transfers#state-change");
        result.Metadata.Should().ContainKey("provider")
            .WhoseValue.Should().Be("wise");
        result.Metadata.Should().ContainKey("currentState")
            .WhoseValue.Should().Be("processing");
        result.Metadata.Should().ContainKey("previousState")
            .WhoseValue.Should().Be("incoming_payment_waiting");
        result.Metadata.Should().ContainKey("transferId")
            .WhoseValue.Should().Be("11223344");
    }

    [Fact]
    public void VerifyWebhookSignature_ShouldReturnTrue_WhenSignatureIsValid()
    {
        // Arrange - sign the payload with our test RSA private key
        var payload = "{\"event_type\":\"transfers#state-change\",\"data\":{}}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signatureBytes = _rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var signatureBase64 = Convert.ToBase64String(signatureBytes);

        // Act
        var result = _provider.VerifyWebhookSignature(payload, signatureBase64);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyWebhookSignature_ShouldReturnFalse_WhenSignatureIsInvalid()
    {
        // Arrange - use a different payload to create a mismatched signature
        var payload = "{\"event_type\":\"transfers#state-change\"}";
        var wrongPayload = "{\"event_type\":\"wrong\"}";
        var wrongBytes = Encoding.UTF8.GetBytes(wrongPayload);
        var signatureBytes = _rsa.SignData(wrongBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var signatureBase64 = Convert.ToBase64String(signatureBytes);

        // Act
        var result = _provider.VerifyWebhookSignature(payload, signatureBase64);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyWebhookSignature_ShouldReturnFalse_WhenSignatureIsGarbage()
    {
        var payload = "{\"event_type\":\"transfers#state-change\"}";
        var garbageSignature = "not-valid-base64!!!";

        var result = _provider.VerifyWebhookSignature(payload, garbageSignature);

        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyWebhookSignature_ShouldReturnFalse_WhenPublicKeyNotConfigured()
    {
        // Arrange - provider without webhook public key should REJECT (fail-closed)
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(x => x["Wise:ApiToken"]).Returns("test-token");

        var freshHttpClient = new HttpClient(_mockHttpHandler.Object)
        {
            BaseAddress = new Uri("https://api.transferwise.com")
        };
        var freshCache = new MemoryCache(new MemoryCacheOptions());

        var provider = new WiseProvider(
            freshHttpClient,
            mockConfig.Object,
            _mockLogger.Object,
            freshCache
        );

        // Act
        var result = provider.VerifyWebhookSignature("{}", "any-signature");

        // Assert - SECURITY: fail-closed when no key configured
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetExchangeRateAsync_ShouldReturnRate()
    {
        // Arrange - Wise returns an array of rate objects
        var rateResponse = new[]
        {
            new { rate = 1500.50m, source = "USD", target = "NGN", time = DateTime.UtcNow }
        };

        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(
                JsonSerializer.Serialize(rateResponse),
                Encoding.UTF8,
                "application/json")
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
        result.Should().Be(1500.50m);
    }

    [Fact]
    public async Task GetExchangeRateAsync_ShouldUseCachedRate()
    {
        // Arrange - first call fetches, second should use cache
        var rateResponse = new[]
        {
            new { rate = 1500m, source = "USD", target = "NGN", time = DateTime.UtcNow }
        };

        var callCount = 0;
        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(() =>
            {
                callCount++;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(
                        JsonSerializer.Serialize(rateResponse),
                        Encoding.UTF8,
                        "application/json")
                };
            });

        // Act - call twice
        var result1 = await _provider.GetExchangeRateAsync("USD", "NGN", 100m);
        var result2 = await _provider.GetExchangeRateAsync("USD", "NGN", 100m);

        // Assert - HTTP should only be called once (second is cached)
        result1.Should().Be(1500m);
        result2.Should().Be(1500m);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task GetExchangeRateAsync_ShouldReturnZero_WhenApiFails()
    {
        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("Network error"));

        var result = await _provider.GetExchangeRateAsync("USD", "NGN", 100m);

        result.Should().Be(0m);
    }

    [Theory]
    [InlineData("USD", "US")]
    [InlineData("GBP", "GB")]
    [InlineData("EUR", "DE")]
    [InlineData("NGN", "NG")]
    [InlineData("KES", "KE")]
    [InlineData("GHS", "GH")]
    [InlineData("ZAR", "ZA")]
    [InlineData("CAD", "CA")]
    [InlineData("AUD", "AU")]
    [InlineData("JPY", "JP")]
    [InlineData("INR", "IN")]
    [InlineData("BRL", "BR")]
    [InlineData("TZS", "TZ")]
    [InlineData("UGX", "UG")]
    [InlineData("UNKNOWN", "US")]
    public void InferCountryFromCurrency_ShouldMapCorrectly(string currency, string expectedCountry)
    {
        var result = WiseProvider.InferCountryFromCurrency(currency);
        result.Should().Be(expectedCountry);
    }

    [Theory]
    [InlineData("outgoing_payment_sent", true)]
    [InlineData("processing", false)]
    [InlineData("incoming_payment_waiting", false)]
    [InlineData("bounced_back", false)]
    [InlineData("cancelled", false)]
    [InlineData(null, false)]
    public void IsCompletedState_ShouldIdentifyCompletedStates(string? state, bool expected)
    {
        WiseProvider.IsCompletedState(state).Should().Be(expected);
    }

    [Fact]
    public async Task NormalizeWebhookAsync_ShouldFallbackToTransferId_WhenResourceIdMissing()
    {
        var payload = new
        {
            event_type = "transfers#state-change",
            data = new
            {
                transfer_id = 77889900L,
                current_state = "processing",
                source_currency = "USD",
                source_amount = 250m,
                target_currency = "GBP",
                target_amount = 200m
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        var result = await _provider.NormalizeWebhookAsync(jsonPayload);

        result.TransactionId.Should().Be("77889900");
    }

    /// <summary>
    /// Helper to export RSA public key as PEM string for test configuration.
    /// </summary>
    private static string ExportPublicKeyPem(RSA rsa)
    {
        var publicKeyBytes = rsa.ExportSubjectPublicKeyInfo();
        var sb = new StringBuilder();
        sb.AppendLine("-----BEGIN PUBLIC KEY-----");
        sb.AppendLine(Convert.ToBase64String(publicKeyBytes, Base64FormattingOptions.InsertLineBreaks));
        sb.AppendLine("-----END PUBLIC KEY-----");
        return sb.ToString();
    }
}
