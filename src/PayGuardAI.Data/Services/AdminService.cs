using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Admin dashboard service implementation — organization, team, API keys, webhooks, analytics.
/// </summary>
public class AdminService : IAdminService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AdminService> _logger;

    public AdminService(ApplicationDbContext db, ILogger<AdminService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Organization Settings ─────────────────────────────────────────────────

    public async Task<OrganizationSettings> GetOrCreateSettingsAsync(string tenantId, CancellationToken ct = default)
    {
        var settings = await _db.OrganizationSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (settings != null) return settings;

        settings = new OrganizationSettings { TenantId = tenantId };
        _db.OrganizationSettings.Add(settings);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Created default organization settings for tenant {TenantId}", tenantId);
        return settings;
    }

    public async Task<OrganizationSettings> UpdateSettingsAsync(OrganizationSettings settings, string updatedBy, CancellationToken ct = default)
    {
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedBy = updatedBy;
        _db.OrganizationSettings.Update(settings);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Updated organization settings for tenant {TenantId} by {User}", settings.TenantId, updatedBy);
        return settings;
    }

    // ── Team Members ──────────────────────────────────────────────────────────

    public async Task<List<TeamMember>> GetTeamMembersAsync(string tenantId, CancellationToken ct = default)
        => await _db.TeamMembers.Where(m => m.TenantId == tenantId).OrderBy(m => m.DisplayName).ToListAsync(ct);

    public async Task<TeamMember> InviteTeamMemberAsync(string tenantId, string email, string displayName, string role, CancellationToken ct = default)
    {
        var existing = await _db.TeamMembers.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Email == email, ct);
        if (existing != null)
            throw new InvalidOperationException($"A team member with email {email} already exists.");

        var member = new TeamMember
        {
            TenantId = tenantId,
            Email = email,
            DisplayName = displayName,
            Role = role,
            Status = "invited"
        };
        _db.TeamMembers.Add(member);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Invited team member {Email} as {Role} for tenant {TenantId}", email, role, tenantId);
        return member;
    }

    public async Task UpdateTeamMemberRoleAsync(Guid memberId, string newRole, CancellationToken ct = default)
    {
        var member = await _db.TeamMembers.FindAsync([memberId], ct)
            ?? throw new InvalidOperationException("Team member not found.");
        member.Role = newRole;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Updated team member {MemberId} role to {Role}", memberId, newRole);
    }

    public async Task RemoveTeamMemberAsync(Guid memberId, CancellationToken ct = default)
    {
        var member = await _db.TeamMembers.FindAsync([memberId], ct)
            ?? throw new InvalidOperationException("Team member not found.");
        _db.TeamMembers.Remove(member);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Removed team member {MemberId} ({Email})", memberId, member.Email);
    }

    // ── API Keys ──────────────────────────────────────────────────────────────

    public async Task<List<ApiKey>> GetApiKeysAsync(string tenantId, CancellationToken ct = default)
        => await _db.ApiKeys.Where(k => k.TenantId == tenantId).OrderByDescending(k => k.CreatedAt).ToListAsync(ct);

    public async Task<(ApiKey key, string rawKey)> GenerateApiKeyAsync(string tenantId, string name, string createdBy, CancellationToken ct = default)
    {
        var (entity, rawKey) = ApiKey.Generate(tenantId, name, createdBy);
        _db.ApiKeys.Add(entity);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Generated API key {KeyPrefix}... for tenant {TenantId}", entity.KeyPrefix, tenantId);
        return (entity, rawKey);
    }

    public async Task RevokeApiKeyAsync(Guid keyId, CancellationToken ct = default)
    {
        var key = await _db.ApiKeys.FindAsync([keyId], ct)
            ?? throw new InvalidOperationException("API key not found.");
        key.IsActive = false;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Revoked API key {KeyPrefix}... ({KeyId})", key.KeyPrefix, keyId);
    }

    // ── Webhook Endpoints ─────────────────────────────────────────────────────

    public async Task<List<WebhookEndpoint>> GetWebhookEndpointsAsync(string tenantId, CancellationToken ct = default)
        => await _db.WebhookEndpoints.Where(e => e.TenantId == tenantId).OrderByDescending(e => e.CreatedAt).ToListAsync(ct);

    public async Task<WebhookEndpoint> AddWebhookEndpointAsync(WebhookEndpoint endpoint, CancellationToken ct = default)
    {
        _db.WebhookEndpoints.Add(endpoint);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Added webhook endpoint {Url} for tenant {TenantId}", endpoint.Url, endpoint.TenantId);
        return endpoint;
    }

    public async Task<WebhookEndpoint> UpdateWebhookEndpointAsync(WebhookEndpoint endpoint, CancellationToken ct = default)
    {
        endpoint.UpdatedAt = DateTime.UtcNow;
        _db.WebhookEndpoints.Update(endpoint);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Updated webhook endpoint {EndpointId}", endpoint.Id);
        return endpoint;
    }

    public async Task DeleteWebhookEndpointAsync(Guid endpointId, CancellationToken ct = default)
    {
        var endpoint = await _db.WebhookEndpoints.FindAsync([endpointId], ct)
            ?? throw new InvalidOperationException("Webhook endpoint not found.");
        _db.WebhookEndpoints.Remove(endpoint);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Deleted webhook endpoint {EndpointId} ({Url})", endpointId, endpoint.Url);
    }

    // ── Usage Analytics ───────────────────────────────────────────────────────

    public async Task<UsageAnalytics> GetUsageAnalyticsAsync(string tenantId, int days = 30, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-days);

        var transactions = await _db.Transactions
            .Include(t => t.RiskAnalysis)
            .Where(t => t.CreatedAt >= since)
            .ToListAsync(ct);

        var analytics = new UsageAnalytics
        {
            TotalTransactions = await _db.Transactions.CountAsync(ct),
            TransactionsThisPeriod = transactions.Count,
            HighRiskCount = transactions.Count(t => t.RiskAnalysis?.RiskLevel >= RiskLevel.High),
            ReviewedCount = transactions.Count(t => t.RiskAnalysis?.ReviewStatus == ReviewStatus.Approved 
                                                  || t.RiskAnalysis?.ReviewStatus == ReviewStatus.Rejected),
            AverageRiskScore = transactions.Any() 
                ? transactions.Where(t => t.RiskAnalysis != null).Average(t => t.RiskAnalysis!.RiskScore) 
                : 0,
            TotalVolume = transactions.Sum(t => t.Amount),
            DailyTransactions = transactions
                .GroupBy(t => t.CreatedAt.Date)
                .Select(g => new DailyCount { Date = g.Key, Count = g.Count() })
                .OrderBy(d => d.Date)
                .ToList(),
            RiskDistribution = transactions
                .Where(t => t.RiskAnalysis != null)
                .GroupBy(t => t.RiskAnalysis!.RiskLevel.ToString())
                .Select(g => new RiskDistribution { Level = g.Key, Count = g.Count() })
                .OrderBy(r => r.Level)
                .ToList(),
            TopCorridors = transactions
                .GroupBy(t => $"{t.SourceCountry} → {t.DestinationCountry}")
                .Select(g => new TopCorridor { Corridor = g.Key, Count = g.Count(), Volume = g.Sum(t => t.Amount) })
                .OrderByDescending(c => c.Count)
                .Take(5)
                .ToList()
        };

        return analytics;
    }
}
