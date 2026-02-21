using System.Security.Cryptography;

namespace PayGuardAI.Core.Entities;

/// <summary>
/// API key for programmatic access to PayGuard AI webhooks and API.
/// </summary>
public class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;           // "Production", "Staging"
    public string KeyPrefix { get; set; } = string.Empty;       // First 8 chars for display (pg_live_xxxx...)
    public string KeyHash { get; set; } = string.Empty;         // SHA-256 hash of the full key
    public string[] Scopes { get; set; } = ["webhooks:write", "transactions:read"];
    public bool IsActive { get; set; } = true;
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";

    /// <summary>
    /// Generate a new API key and return the raw key (only shown once).
    /// </summary>
    public static (ApiKey entity, string rawKey) Generate(string tenantId, string name, string createdBy)
    {
        var rawBytes = RandomNumberGenerator.GetBytes(32);
        var rawKey = $"pg_live_{Convert.ToBase64String(rawBytes).Replace("+", "").Replace("/", "").Replace("=", "")[..40]}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawKey)));

        var entity = new ApiKey
        {
            TenantId = tenantId,
            Name = name,
            KeyPrefix = rawKey[..16],
            KeyHash = hash,
            CreatedBy = createdBy
        };

        return (entity, rawKey);
    }
}
