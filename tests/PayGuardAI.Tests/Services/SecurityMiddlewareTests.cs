using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using PayGuardAI.Web.Services;

namespace PayGuardAI.Tests.Services;

public class SecurityHeadersMiddlewareTests
{
    private readonly SecurityHeadersMiddleware _middleware;
    private readonly DefaultHttpContext _context;
    private bool _nextCalled;

    public SecurityHeadersMiddlewareTests()
    {
        _nextCalled = false;
        _middleware = new SecurityHeadersMiddleware(_ =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        });
        _context = new DefaultHttpContext();
    }

    [Fact]
    public async Task Should_Add_XFrameOptions_Deny()
    {
        await _middleware.InvokeAsync(_context);

        _context.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Add_XContentTypeOptions_Nosniff()
    {
        await _middleware.InvokeAsync(_context);

        _context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
    }

    [Fact]
    public async Task Should_Add_XXssProtection()
    {
        await _middleware.InvokeAsync(_context);

        _context.Response.Headers["X-XSS-Protection"].ToString().Should().Be("1; mode=block");
    }

    [Fact]
    public async Task Should_Add_ReferrerPolicy()
    {
        await _middleware.InvokeAsync(_context);

        _context.Response.Headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
    }

    [Fact]
    public async Task Should_Add_PermissionsPolicy()
    {
        await _middleware.InvokeAsync(_context);

        var permissionsPolicy = _context.Response.Headers["Permissions-Policy"].ToString();
        permissionsPolicy.Should().Contain("camera=()");
        permissionsPolicy.Should().Contain("microphone=()");
        permissionsPolicy.Should().Contain("geolocation=()");
    }

    [Fact]
    public async Task Should_Add_ContentSecurityPolicy()
    {
        await _middleware.InvokeAsync(_context);

        var csp = _context.Response.Headers["Content-Security-Policy"].ToString();
        csp.Should().Contain("default-src 'self'");
        csp.Should().Contain("frame-ancestors 'none'");
        csp.Should().Contain("connect-src 'self' ws: wss:");
    }

    [Fact]
    public async Task Should_Add_CacheControl_For_ApiEndpoints()
    {
        _context.Request.Path = "/api/webhooks/afriex";

        await _middleware.InvokeAsync(_context);

        _context.Response.Headers["Cache-Control"].ToString().Should().Contain("no-store");
        _context.Response.Headers["Pragma"].ToString().Should().Be("no-cache");
    }

    [Fact]
    public async Task Should_Not_Add_CacheControl_For_NonApiEndpoints()
    {
        _context.Request.Path = "/";

        await _middleware.InvokeAsync(_context);

        _context.Response.Headers.ContainsKey("Cache-Control").Should().BeFalse();
    }

    [Fact]
    public async Task Should_Always_Call_Next()
    {
        _context.Request.Path = "/any-path";

        await _middleware.InvokeAsync(_context);

        _nextCalled.Should().BeTrue();
    }
}

public class InputValidationMiddlewareTests
{
    private readonly Mock<ILogger<InputValidationMiddleware>> _logger;
    private bool _nextCalled;

    public InputValidationMiddlewareTests()
    {
        _logger = new Mock<ILogger<InputValidationMiddleware>>();
    }

    private InputValidationMiddleware CreateMiddleware()
    {
        _nextCalled = false;
        return new InputValidationMiddleware(_ =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        }, _logger.Object);
    }

    [Fact]
    public async Task Should_Allow_NonApi_Requests()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Path = "/";
        context.Request.Method = "POST";

        await middleware.InvokeAsync(context);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Allow_Get_Requests()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/webhooks/health";
        context.Request.Method = "GET";

        await middleware.InvokeAsync(context);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Reject_NonJson_ContentType()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/webhooks/afriex";
        context.Request.Method = "POST";
        context.Request.ContentType = "text/xml";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(415);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Should_Allow_Json_ContentType()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/webhooks/afriex";
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";

        await middleware.InvokeAsync(context);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Reject_Oversized_Payload()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/webhooks/afriex";
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = 2_000_000; // 2MB > 1MB limit
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(413);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Should_Allow_Normal_Size_Payload()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/webhooks/afriex";
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = 500; // Well within limits

        await middleware.InvokeAsync(context);

        _nextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData("' OR '1'='1")]
    [InlineData("'; DROP TABLE users;--")]
    [InlineData("'; DELETE FROM transactions;--")]
    [InlineData("<script>alert('xss')</script>")]
    public async Task Should_Block_Suspicious_QueryStrings(string maliciousQuery)
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/webhooks/afriex";
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.QueryString = new QueryString($"?q={maliciousQuery}");
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(400);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Should_Enforce_Smaller_Limit_For_Simulate_Endpoint()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/webhooks/simulate";
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = 50_000; // 50KB > 10KB limit for simulate
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(413);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Should_Allow_Request_Without_ContentLength()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/webhooks/afriex";
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        // No ContentLength set

        await middleware.InvokeAsync(context);

        _nextCalled.Should().BeTrue();
    }
}

public class ApiKeyAuthenticationMiddlewareTests
{
    [Fact]
    public async Task Should_Skip_NonApi_Paths()
    {
        var nextCalled = false;
        var logger = new Mock<ILogger<ApiKeyAuthenticationMiddleware>>();
        var middleware = new ApiKeyAuthenticationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, logger.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/home";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Skip_Health_Endpoint()
    {
        var nextCalled = false;
        var logger = new Mock<ILogger<ApiKeyAuthenticationMiddleware>>();
        var middleware = new ApiKeyAuthenticationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, logger.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/webhooks/health";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Skip_Simulate_Endpoint()
    {
        var nextCalled = false;
        var logger = new Mock<ILogger<ApiKeyAuthenticationMiddleware>>();
        var middleware = new ApiKeyAuthenticationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, logger.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/webhooks/simulate";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Allow_Request_Without_ApiKey_Header()
    {
        // API key is optional — webhooks primarily use signature verification
        var nextCalled = false;
        var logger = new Mock<ILogger<ApiKeyAuthenticationMiddleware>>();
        var middleware = new ApiKeyAuthenticationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, logger.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/webhooks/afriex";
        // No X-API-Key header

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }
}

public class IpWhitelistMiddlewareTests
{
    [Fact]
    public async Task Should_Skip_NonApi_Paths()
    {
        var nextCalled = false;
        var logger = new Mock<ILogger<IpWhitelistMiddleware>>();
        var middleware = new IpWhitelistMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, logger.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/home";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Skip_Health_Endpoint()
    {
        var nextCalled = false;
        var logger = new Mock<ILogger<IpWhitelistMiddleware>>();
        var middleware = new IpWhitelistMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, logger.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/webhooks/health";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Allow_When_No_TenantId()
    {
        var nextCalled = false;
        var logger = new Mock<ILogger<IpWhitelistMiddleware>>();
        var middleware = new IpWhitelistMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, logger.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/webhooks/afriex";
        // No TenantId in Items

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }
}
