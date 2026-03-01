using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.EntityFrameworkCore;
using PayGuardAI.Data;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Periodically re-validates the authentication state of a Blazor Server circuit.
///
/// Problem: Blazor Server caches the auth state from the initial HTTP connection for the
/// lifetime of the SignalR circuit. When the auth cookie expires (e.g. user is idle for 60+
/// minutes), the circuit still thinks the user is authenticated — leaving the navbar and
/// protected pages visible.
///
/// Solution: Every <see cref="RevalidationInterval"/> this provider checks the DB to confirm
/// the user's TeamMember record still exists and is active. When it returns false, the base
/// class marks the user as anonymous and raises <c>AuthenticationStateChanged</c>, which
/// causes <c>AuthorizeView</c> and <c>AuthorizeRouteView</c> to re-render with the
/// unauthenticated state — hiding the navbar and triggering <c>RedirectToLogin</c>.
/// </summary>
public class PayGuardRevalidatingAuthStateProvider : RevalidatingServerAuthenticationStateProvider
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PayGuardRevalidatingAuthStateProvider(
        ILoggerFactory loggerFactory,
        IServiceScopeFactory scopeFactory)
        : base(loggerFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Re-validate every 2 minutes. This catches expired cookies within ~2 minutes
    /// of the user returning to the tab, without excessive DB queries.
    /// </summary>
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(2);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        // If user isn't authenticated, nothing to validate
        if (authenticationState.User.Identity?.IsAuthenticated != true)
            return false;

        var email = authenticationState.User.FindFirst(ClaimTypes.Email)?.Value
                    ?? authenticationState.User.FindFirst(ClaimTypes.Name)?.Value;

        if (string.IsNullOrEmpty(email))
            return false;

        try
        {
            // Create a new scope because this provider outlives individual request scopes
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Same check as OnValidatePrincipal — verify the user still exists and is active
            var memberExists = await db.TeamMembers
                .IgnoreQueryFilters()
                .AnyAsync(
                    t => t.Email.ToLower() == email.ToLower() && t.Status == "active",
                    cancellationToken);

            return memberExists;
        }
        catch (Exception)
        {
            // If we can't reach the DB, keep the user authenticated to avoid
            // false logouts during transient network blips
            return true;
        }
    }
}
