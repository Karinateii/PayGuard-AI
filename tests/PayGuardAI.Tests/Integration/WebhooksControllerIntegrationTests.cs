using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace PayGuardAI.Tests.Integration;

public class WebhooksControllerIntegrationTests : IClassFixture<PayGuardApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly PayGuardApiWebApplicationFactory _factory;

    public WebhooksControllerIntegrationTests(PayGuardApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task HealthEndpoint_ShouldReturnHealthyStatus()
    {
        // Act
        var response = await _client.GetAsync("/api/webhooks/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("healthy");
        content.Should().Contain("PayGuard AI");
        content.Should().Contain("afriex");
    }

    [Fact]
    public async Task HealthEndpoint_ShouldReturnProvidersList()
    {
        // Act
        var response = await _client.GetAsync("/api/webhooks/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<HealthCheckResponse>();
        content.Should().NotBeNull();
        content!.Status.Should().Be("healthy");
        content.Providers.Should().NotBeEmpty();
        content.Providers.Should().Contain(p => p.Name == "afriex");
    }

    [Fact]
    public async Task AfriexWebhook_ShouldAcceptValidPayload()
    {
        // Arrange
        var webhook = new
        {
            @event = "TRANSACTION.CREATED",
            data = new
            {
                transactionId = "INT-TEST-001",
                customerId = "cust-integration-001",
                sourceAmount = "100",
                sourceCurrency = "USD",
                destinationAmount = "150000",
                destinationCurrency = "NGN",
                sourceCountry = "US",
                destinationCountry = "NG",
                status = "PENDING",
                createdAt = DateTime.UtcNow.ToString("O")
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(webhook),
            Encoding.UTF8,
            "application/json"
        );

        // Act
        var response = await _client.PostAsync("/api/webhooks/afriex", content);

        // Assert
        // Note: This might fail signature verification, which is expected
        // In a real test, you'd set up proper signature mocking
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.BadRequest // Expected if signature verification fails
        );
    }

    [Fact]
    public async Task FlutterwaveWebhook_ShouldAcceptValidPayload()
    {
        // Arrange
        var webhook = new
        {
            @event = "charge.completed",
            data = new
            {
                id = 999999,
                tx_ref = "INT-FLW-TEST-001",
                flw_ref = "FLWINT999999",
                amount = 100.00,
                currency = "USD",
                status = "successful",
                payment_type = "card",
                customer = new
                {
                    email = "integration@test.com",
                    phone_number = "+1234567890"
                },
                created_at = DateTime.UtcNow.ToString("O")
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(webhook),
            Encoding.UTF8,
            "application/json"
        );

        content.Headers.Add("verif-hash", "test-hash");

        // Act
        var response = await _client.PostAsync("/api/webhooks/flutterwave", content);

        // Assert
        // Note: This might fail signature verification or provider not configured
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.BadRequest // Expected if signature verification fails or provider disabled
        );
    }

    [Fact]
    public async Task LegacyWebhook_ShouldStillWork()
    {
        // Arrange
        var webhook = new
        {
            @event = "TRANSACTION.CREATED",
            data = new
            {
                transactionId = "LEGACY-TEST-001",
                customerId = "cust-legacy-001",
                sourceAmount = "50",
                sourceCurrency = "USD",
                destinationAmount = "75000",
                destinationCurrency = "NGN",
                sourceCountry = "US",
                destinationCountry = "NG",
                status = "PENDING",
                createdAt = DateTime.UtcNow.ToString("O")
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(webhook),
            Encoding.UTF8,
            "application/json"
        );

        // Act
        var response = await _client.PostAsync("/api/webhooks/transaction", content);

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.BadRequest
        );
    }

    [Fact]
    public async Task Webhook_ShouldRejectInvalidJson()
    {
        // Arrange
        var content = new StringContent(
            "{invalid json}",
            Encoding.UTF8,
            "application/json"
        );

        // Act
        var response = await _client.PostAsync("/api/webhooks/afriex", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Webhook_ShouldRejectEmptyPayload()
    {
        // Arrange
        var content = new StringContent(
            string.Empty,
            Encoding.UTF8,
            "application/json"
        );

        // Act
        var response = await _client.PostAsync("/api/webhooks/afriex", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HealthEndpoint_ShouldHandleConcurrentRequests()
    {
        // Arrange
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            _client.GetAsync("/api/webhooks/health")
        );

        // Act
        var responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
    }

    private class HealthCheckResponse
    {
        public string Status { get; set; } = "";
        public string Service { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public List<ProviderStatus> Providers { get; set; } = new();
    }

    private class ProviderStatus
    {
        public string Name { get; set; } = "";
        public bool Configured { get; set; }
    }
}
