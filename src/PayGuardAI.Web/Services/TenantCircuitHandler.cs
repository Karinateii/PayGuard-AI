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

    public TenantCircuitHandler(
        ITenantContext tenantContext,
        AuthenticationStateProvider authStateProvider,
        IConfiguration configuration)
    {
        _tenantContext = tenantContext;
        _authStateProvider = authStateProvider;
        _configuration = configuration;
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
            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;

            var tenantId = user?.FindFirst("tenant_id")?.Value;
            if (!string.IsNullOrEmpty(tenantId))
            {
                _tenantContext.TenantId = tenantId;
            }
            else
            {
                // Fall back to config default (same as TenantResolutionMiddleware)
                _tenantContext.TenantId = _configuration["MultiTenancy:DefaultTenantId"] ?? "afriex-demo";
            }
        }
        catch
        {
            // If auth state is not available yet, use default
            _tenantContext.TenantId = _configuration["MultiTenancy:DefaultTenantId"] ?? "afriex-demo";
        }
    }
}
