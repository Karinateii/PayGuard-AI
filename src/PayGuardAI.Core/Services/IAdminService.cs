using PayGuardAI.Core.Entities;

namespace PayGuardAI.Core.Services;

/// <summary>
/// Admin dashboard service — organization settings, team, API keys, webhooks, usage analytics.
/// </summary>
public interface IAdminService
{
    // Organization
    Task<OrganizationSettings> GetOrCreateSettingsAsync(string tenantId, CancellationToken ct = default);
    Task<OrganizationSettings> UpdateSettingsAsync(OrganizationSettings settings, string updatedBy, CancellationToken ct = default);

    // Team Members
    Task<List<TeamMember>> GetTeamMembersAsync(string tenantId, CancellationToken ct = default);
    Task<(TeamMember member, bool emailSent)> InviteTeamMemberAsync(string tenantId, string email, string displayName, string role, CancellationToken ct = default);
    Task UpdateTeamMemberRoleAsync(Guid memberId, string newRole, CancellationToken ct = default);
    Task RemoveTeamMemberAsync(Guid memberId, CancellationToken ct = default);

    // API Keys
    Task<List<ApiKey>> GetApiKeysAsync(string tenantId, CancellationToken ct = default);
    Task<(ApiKey key, string rawKey)> GenerateApiKeyAsync(string tenantId, string name, string createdBy, CancellationToken ct = default);
    Task RevokeApiKeyAsync(Guid keyId, CancellationToken ct = default);

    // Webhook Endpoints
    Task<List<WebhookEndpoint>> GetWebhookEndpointsAsync(string tenantId, CancellationToken ct = default);
    Task<WebhookEndpoint> AddWebhookEndpointAsync(WebhookEndpoint endpoint, CancellationToken ct = default);
    Task<WebhookEndpoint> UpdateWebhookEndpointAsync(WebhookEndpoint endpoint, CancellationToken ct = default);
    Task DeleteWebhookEndpointAsync(Guid endpointId, CancellationToken ct = default);

    // Usage Analytics
    Task<UsageAnalytics> GetUsageAnalyticsAsync(string tenantId, int days = 30, CancellationToken ct = default);
}

/// <summary>
/// Usage analytics data for the admin dashboard.
/// </summary>
public class UsageAnalytics
{
    public int TotalTransactions { get; set; }
    public int TransactionsThisPeriod { get; set; }
    public int HighRiskCount { get; set; }
    public int ReviewedCount { get; set; }
    public double AverageRiskScore { get; set; }
    public decimal TotalVolume { get; set; }
    public List<DailyCount> DailyTransactions { get; set; } = [];
    public List<RiskDistribution> RiskDistribution { get; set; } = [];
    public List<TopCorridor> TopCorridors { get; set; } = [];
}

public class DailyCount
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

public class RiskDistribution
{
    public string Level { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class TopCorridor
{
    public string Corridor { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Volume { get; set; }
}
