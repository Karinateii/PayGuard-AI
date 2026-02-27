using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace PayGuardAI.Web.Controllers;

/// <summary>
/// API version discovery endpoint.
/// Returns the current and supported API versions for client integration.
/// Endpoint: GET /api/version
/// </summary>
[ApiController]
[ApiVersionNeutral] // This endpoint works regardless of version
[Route("api/[controller]")]
[AllowAnonymous]
public class VersionController : ControllerBase
{
    /// <summary>
    /// Returns API version information including current version, all supported versions,
    /// and deprecation notices. Use this to discover available versions and plan migrations.
    /// </summary>
    /// <response code="200">Version information returned successfully</response>
    [HttpGet]
    [ProducesResponseType(typeof(ApiVersionInfo), StatusCodes.Status200OK)]
    public IActionResult GetVersion()
    {
        return Ok(new ApiVersionInfo
        {
            Current = "1.0",
            Supported = ["1.0"],
            Deprecated = [],
            Sunset = [],
            UrlFormat = "/api/v{version}/{controller}/{action}",
            HeaderFormat = "api-version: {version}",
            Documentation = "https://payguard-ai-production.up.railway.app/swagger",
            Notes = "API versioning uses URL segment strategy. " +
                    "Unversioned routes (/api/webhooks) default to v1.0 for backward compatibility."
        });
    }
}

/// <summary>
/// API version discovery response model.
/// </summary>
public class ApiVersionInfo
{
    /// <summary>Current recommended API version</summary>
    /// <example>1.0</example>
    public string Current { get; set; } = "1.0";

    /// <summary>All actively supported API versions</summary>
    /// <example>["1.0"]</example>
    public string[] Supported { get; set; } = [];

    /// <summary>Deprecated versions (still functional but will be removed)</summary>
    /// <example>[]</example>
    public string[] Deprecated { get; set; } = [];

    /// <summary>Sunset versions with removal dates</summary>
    /// <example>[]</example>
    public Dictionary<string, string> Sunset { get; set; } = [];

    /// <summary>URL format template for versioned requests</summary>
    /// <example>/api/v{version}/{controller}/{action}</example>
    public string UrlFormat { get; set; } = "";

    /// <summary>Header format template for version negotiation</summary>
    /// <example>api-version: {version}</example>
    public string HeaderFormat { get; set; } = "";

    /// <summary>Link to full API documentation</summary>
    /// <example>https://payguard-ai-production.up.railway.app/swagger</example>
    public string Documentation { get; set; } = "";

    /// <summary>Human-readable versioning notes</summary>
    public string Notes { get; set; } = "";
}
