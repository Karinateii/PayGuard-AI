using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using PayGuardAI.Web.Models;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Extension methods for configuring authentication with feature flag support
/// Supports both Demo authentication (for development) and OAuth 2.0 / OpenID Connect (for production)
/// </summary>
public static class AuthenticationConfiguration
{
    /// <summary>
    /// Configure authentication based on feature flags
    /// When OAuthEnabled=true, uses OAuth 2.0 / OpenID Connect (Google, Azure AD, etc.)
    /// When OAuthEnabled=false, uses Demo authentication handler
    /// </summary>
    public static IServiceCollection AddPayGuardAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var oAuthEnabled = configuration.IsOAuthEnabled();
        
        var oAuthSettings = configuration.GetSection("OAuth").Get<OAuthSettings>()
                            ?? new OAuthSettings();

        if (oAuthEnabled && oAuthSettings.IsValid())
        {
            // Production OAuth 2.0 / OpenID Connect authentication
            ConfigureOAuthAuthentication(services, configuration, oAuthSettings);
        }
        else
        {
            // Development Demo authentication
            ConfigureDemoAuthentication(services);
        }

        return services;
    }

    private static void ConfigureDemoAuthentication(IServiceCollection services)
    {
        services.AddAuthentication("Demo")
            .AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>("Demo", _ => { });

        services.AddCascadingAuthenticationState();
    }

    private static void ConfigureOAuthAuthentication(
        IServiceCollection services,
        IConfiguration configuration,
        OAuthSettings settings)
    {
        // Cookie authentication for session management
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.Name = "PayGuardAuth";
            options.Cookie.HttpOnly = true;
            // SameAsRequest works behind Railway's reverse proxy (which forwards HTTP internally)
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(settings.CookieExpirationMinutes);
            options.SlidingExpiration = settings.UseSlidingExpiration;
            options.LoginPath = "/login";
            options.LogoutPath = "/logout";
            options.AccessDeniedPath = "/access-denied";
        })
        .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            options.Authority = settings.Authority;
            options.ClientId = settings.ClientId;
            options.ClientSecret = settings.ClientSecret;
            options.ResponseType = "code";
            options.UsePkce = true;
            options.CallbackPath = "/signin-oidc";
            options.SignedOutCallbackPath = "/signout-callback-oidc";
            
            // Configure scopes
            options.Scope.Clear();
            foreach (var scope in settings.Scopes)
            {
                options.Scope.Add(scope);
            }

            // Save tokens for API calls
            options.SaveTokens = settings.SaveTokens;
            options.GetClaimsFromUserInfoEndpoint = true;

            // Map claims — Google uses different claim types than Azure AD
            options.TokenValidationParameters = new()
            {
                NameClaimType = "name",
                RoleClaimType = ClaimTypes.Role,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true
            };

            // Events for logging and role assignment
            options.Events = new OpenIdConnectEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var loggerFactory = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger("PayGuard.OAuth");
                    logger.LogError(context.Exception, "OAuth authentication failed");
                    
                    context.Response.Redirect("/login?error=authentication_failed");
                    context.HandleResponse();
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var loggerFactory = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger("PayGuard.OAuth");
                    
                    var userEmail = context.Principal?.FindFirst(ClaimTypes.Email)?.Value
                                    ?? context.Principal?.FindFirst("email")?.Value
                                    ?? context.Principal?.FindFirst("preferred_username")?.Value
                                    ?? "unknown";
                    
                    logger.LogInformation("User authenticated via OAuth: {UserEmail}", userEmail);

                    // Add default roles — Google doesn't provide roles, so we assign them
                    var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                    var defaultRoles = config["Auth:DefaultRoles"] ?? "Reviewer,Manager,Admin,SuperAdmin";
                    
                    if (context.Principal?.Identity is ClaimsIdentity identity)
                    {
                        // Add roles if not already present
                        if (!identity.HasClaim(c => c.Type == ClaimTypes.Role))
                        {
                            foreach (var role in defaultRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            {
                                identity.AddClaim(new Claim(ClaimTypes.Role, role));
                            }
                        }
                        
                        // Ensure Name claim is set (some providers use different claim types)
                        if (!identity.HasClaim(c => c.Type == ClaimTypes.Name))
                        {
                            identity.AddClaim(new Claim(ClaimTypes.Name, userEmail));
                        }

                        // Add tenant claim — future: look up from TeamMember table by email
                        var defaultTenantId = config["MultiTenancy:DefaultTenantId"] ?? "afriex-demo";
                        identity.AddClaim(new Claim("tenant_id", defaultTenantId));
                    }
                    
                    return Task.CompletedTask;
                },
                OnRedirectToIdentityProvider = context =>
                {
                    // Ensure redirect URI uses HTTPS scheme when behind reverse proxy
                    var forwardedProto = context.HttpContext.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
                    if (forwardedProto == "https")
                    {
                        context.ProtocolMessage.RedirectUri = context.ProtocolMessage.RedirectUri
                            .Replace("http://", "https://");
                    }
                    return Task.CompletedTask;
                },
                OnRedirectToIdentityProviderForSignOut = context =>
                {
                    var forwardedProto = context.HttpContext.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
                    if (forwardedProto == "https" && context.ProtocolMessage.PostLogoutRedirectUri != null)
                    {
                        context.ProtocolMessage.PostLogoutRedirectUri = context.ProtocolMessage.PostLogoutRedirectUri
                            .Replace("http://", "https://");
                    }
                    return Task.CompletedTask;
                }
            };
        });

        services.AddCascadingAuthenticationState();
    }

    /// <summary>
    /// Get configured authentication scheme name
    /// </summary>
    public static string GetAuthenticationScheme(this IConfiguration configuration)
    {
        var oauthEnabled = configuration.GetValue<bool>("FeatureFlags:OAuthEnabled");
        return oauthEnabled ? CookieAuthenticationDefaults.AuthenticationScheme : "Demo";
    }
}
