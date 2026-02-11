using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
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
    /// When OAuthEnabled=true, uses OAuth 2.0 / OpenID Connect
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
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
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
            
            // Configure scopes
            options.Scope.Clear();
            foreach (var scope in settings.Scopes)
            {
                options.Scope.Add(scope);
            }

            // Save tokens for API calls
            options.SaveTokens = settings.SaveTokens;
            options.GetClaimsFromUserInfoEndpoint = true;

            // Map claims
            options.TokenValidationParameters = new()
            {
                NameClaimType = "name",
                RoleClaimType = "roles",
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true
            };

            // Events for logging
            options.Events = new OpenIdConnectEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var loggerFactory = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger("PayGuard.OAuth");
                    logger.LogError(context.Exception, "OAuth authentication failed");
                    
                    context.Response.Redirect("/error?message=authentication_failed");
                    context.HandleResponse();
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var loggerFactory = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger("PayGuard.OAuth");
                    
                    var userEmail = context.Principal?.FindFirst("preferred_username")?.Value
                                    ?? context.Principal?.FindFirst("email")?.Value
                                    ?? "unknown";
                    
                    logger.LogInformation("User authenticated: {UserEmail}", userEmail);
                    return Task.CompletedTask;
                },
                OnRedirectToIdentityProvider = context =>
                {
                    // Add tenant hint if using Azure AD multi-tenant
                    if (context.Properties.Items.TryGetValue("tenant", out var tenant))
                    {
                        context.ProtocolMessage.SetParameter("domain_hint", tenant);
                    }
                    return Task.CompletedTask;
                }
            };
        });

        // For Azure AD specific features (optional, enhanced integration)
        if (settings.Provider.Equals("AzureAD", StringComparison.OrdinalIgnoreCase))
        {
            services.AddMicrosoftIdentityWebAppAuthentication(configuration, "OAuth");
        }

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
