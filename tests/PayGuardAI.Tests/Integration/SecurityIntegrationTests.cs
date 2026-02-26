using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;

namespace PayGuardAI.Tests.Integration;

[Collection("Integration")]
public class SecurityIntegrationTests
{
    private readonly HttpClient _client;

    public SecurityIntegrationTests(PayGuardApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task HealthEndpoint_ShouldInclude_SecurityHeaders()
    {
        var response = await _client.GetAsync("/api/webhooks/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().Contain(h => h.Key == "X-Frame-Options");
        response.Headers.GetValues("X-Frame-Options").First().Should().Be("DENY");
    }

    [Fact]
    public async Task HealthEndpoint_ShouldInclude_XContentTypeOptions()
    {
        var response = await _client.GetAsync("/api/webhooks/health");

        response.Headers.Should().Contain(h => h.Key == "X-Content-Type-Options");
        response.Headers.GetValues("X-Content-Type-Options").First().Should().Be("nosniff");
    }

    [Fact]
    public async Task HealthEndpoint_ShouldInclude_ReferrerPolicy()
    {
        var response = await _client.GetAsync("/api/webhooks/health");

        response.Headers.Should().Contain(h => h.Key == "Referrer-Policy");
        response.Headers.GetValues("Referrer-Policy").First().Should().Be("strict-origin-when-cross-origin");
    }

    [Fact]
    public async Task HealthEndpoint_ShouldInclude_XssProtection()
    {
        var response = await _client.GetAsync("/api/webhooks/health");

        response.Headers.Should().Contain(h => h.Key == "X-XSS-Protection");
        response.Headers.GetValues("X-XSS-Protection").First().Should().Be("1; mode=block");
    }

    [Fact]
    public async Task HealthEndpoint_ShouldInclude_PermissionsPolicy()
    {
        var response = await _client.GetAsync("/api/webhooks/health");

        response.Headers.Should().Contain(h => h.Key == "Permissions-Policy");
        var policy = response.Headers.GetValues("Permissions-Policy").First();
        policy.Should().Contain("camera=()");
        policy.Should().Contain("microphone=()");
    }

    [Fact]
    public async Task ApiEndpoint_ShouldInclude_NoCacheHeaders()
    {
        var response = await _client.GetAsync("/api/webhooks/health");

        response.Headers.Should().Contain(h => h.Key == "Cache-Control");
    }

    [Fact]
    public async Task WebhookEndpoint_ShouldReject_XmlContent()
    {
        var content = new StringContent("<xml>test</xml>", System.Text.Encoding.UTF8, "text/xml");

        var response = await _client.PostAsync("/api/webhooks/afriex", content);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task WiseWebhookEndpoint_Exists()
    {
        // Verify the /api/webhooks/wise endpoint is routable
        var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/webhooks/wise", content);

        // Should get 401 (no signature) or 400, not 404 (not found)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SimulateEndpoint_ShouldRequireAuthentication()
    {
        // Simulate endpoint should require authentication (no longer [AllowAnonymous])
        var request = new
        {
            Amount = 100,
            SourceCountry = "US",
            DestinationCountry = "NG"
        };

        var content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(request),
            System.Text.Encoding.UTF8,
            "application/json"
        );

        var response = await _client.PostAsync("/api/webhooks/simulate", content);

        // Unauthenticated request should be redirected to login (302)
        response.StatusCode.Should().Be(HttpStatusCode.Found);
    }

    [Fact]
    public async Task HealthEndpoint_ShouldInclude_ContentSecurityPolicy()
    {
        var response = await _client.GetAsync("/api/webhooks/health");

        // CSP may be in the response content headers or the main headers
        var allHeaders = response.Headers
            .Concat(response.Content.Headers)
            .SelectMany(h => h.Value.Select(v => new { h.Key, Value = v }));

        allHeaders.Should().Contain(h => h.Key == "Content-Security-Policy");
    }
}
