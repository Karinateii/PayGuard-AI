using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Sets TenantContext.TenantId when a Blazor Server circuit connects.
/// 
/// In Blazor Server, the initial page is server-side rendered (SSR) through the HTTP pipeline
/// where TenantResolutionMiddleware sets the tenant from claims. However, when the SignalR
/// circuit connects, a NEW DI scope is created and TenantResolutionMiddleware does not run
/// for WebSocket messages, so the scoped TenantContext starts empty.
/// 
/// This handler reads the user's tenant_id claim and sets TenantContext so that the 
/// DbContext query filters resolve to the correct tenant for the circuit lifetime.
/// </summary>
public class TenantCircuitHandler : CircuitHandler
{
    private readonly ITenantContext _tenantContext;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantCircuitHandler(
        ITenantContext tenantContext,
        AuthenticationStateProvider authStateProvider,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor)
    {
        _tenantContext = tenantContext;
        _authStateProvider = authStateProvider;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }

    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken ct)
    {
        await SetTenantFromClaimsAsync();
        await base.OnCircuitOpenedAsync(circuit, ct);
    }

    public override async Task OnConnectionUpAsync(Circuit circuit, CancellationToken ct)
    {
        // Also re-set on reconnection in case the scope was recycled
        await SetTenantFromClaimsAsync();
        await base.OnConnectionUpAsync(circuit, ct);
    }

    private async Task SetTenantFromClaimsAsync()
    {
        try
        {
            // Check session for SuperAdmin tenant impersonation first
            var impersonated = _httpContextAccessor.HttpContext?.Session.GetString("ImpersonateTenantId");
            if (!string.IsNullOrEmpty(impersonated))
            {
                _tenantContext.TenantId = impersonated;
                return;
            }

            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;

            var tenantId = user?.FindFirst("tenant_id")?.Value;
            if (!string.IsNullOrEmpty(tenantId))
            {
                _tenantContext.TenantId = tenantId;
            }
            else
            {
                _tenantContext.TenantId = _configuration["MultiTenancy:DefaultTenantId"] ?? "afriex-demo";
            }
        }
        catch
        {
            _tenantContext.TenantId = _configuration["MultiTenancy:DefaultTenantId"] ?? "afriex-demo";
        }
    }
}
