namespace PayGuardAI.Web.Models;

/// <summary>
/// OAuth 2.0 / OpenID Connect configuration settings
/// Supports Azure AD, Google, Okta, and custom OIDC providers
/// </summary>
public class OAuthSettings
{
    /// <summary>
    /// OAuth provider type (AzureAD, Google, Okta, Custom)
    /// </summary>
    public string Provider { get; set; } = "AzureAD";

    /// <summary>
    /// Azure AD Tenant ID (for AzureAD provider)
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Application (Client) ID from the identity provider
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client secret for confidential client flow
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Authority URL (e.g., https://login.microsoftonline.com/{tenant})
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// Redirect URI after successful authentication
    /// </summary>
    public string RedirectUri { get; set; } = "/signin-oidc";

    /// <summary>
    /// Post-logout redirect URI
    /// </summary>
    public string PostLogoutRedirectUri { get; set; } = "/";

    /// <summary>
    /// OpenID Connect scopes to request
    /// </summary>
    public string[] Scopes { get; set; } = ["openid", "profile", "email"];

    /// <summary>
    /// Whether to save tokens in authentication properties
    /// </summary>
    public bool SaveTokens { get; set; } = true;

    /// <summary>
    /// Cookie expiration in minutes
    /// </summary>
    public int CookieExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// Whether to use sliding expiration for cookies
    /// </summary>
    public bool UseSlidingExpiration { get; set; } = true;

    /// <summary>
    /// Validate configuration
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(ClientId) 
               && !string.IsNullOrWhiteSpace(ClientSecret)
               && !string.IsNullOrWhiteSpace(Authority);
    }
}
