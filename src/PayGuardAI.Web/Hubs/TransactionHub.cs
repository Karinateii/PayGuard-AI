using Microsoft.AspNetCore.SignalR;
using PayGuardAI.Core.Entities;

namespace PayGuardAI.Web.Hubs;

/// <summary>
/// SignalR hub for real-time transaction updates
/// </summary>
public class TransactionHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
    }
}

/// <summary>
/// DTO for transaction notification
/// </summary>
public record TransactionNotification(
    Guid TransactionId,
    string ExternalId,
    decimal Amount,
    string SourceCountry,
    string DestinationCountry,
    int RiskScore,
    RiskLevel RiskLevel,
    ReviewStatus ReviewStatus,
    string Explanation
);
