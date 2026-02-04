using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PayGuardAI.Core.Services;
using PayGuardAI.Web.Hubs;

namespace PayGuardAI.Web.Controllers;

/// <summary>
/// Webhook controller for receiving Afriex transaction events.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class WebhooksController : ControllerBase
{
    private readonly ITransactionService _transactionService;
    private readonly IHubContext<TransactionHub> _hubContext;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        ITransactionService transactionService,
        IHubContext<TransactionHub> hubContext,
        ILogger<WebhooksController> logger)
    {
        _transactionService = transactionService;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Receives transaction webhooks from Afriex.
    /// Endpoint: POST /api/webhooks/transaction
    /// </summary>
    [HttpPost("transaction")]
    public async Task<IActionResult> ReceiveTransaction(CancellationToken cancellationToken)
    {
        try
        {
            // Read raw payload
            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync(cancellationToken);

            _logger.LogInformation("Received webhook: {PayloadPreview}...", 
                payload.Length > 100 ? payload[..100] : payload);

            // Process the transaction
            var transaction = await _transactionService.ProcessWebhookAsync(payload, cancellationToken);

            _logger.LogInformation("Transaction {TransactionId} processed with risk score {RiskScore}", 
                transaction.Id, transaction.RiskAnalysis?.RiskScore ?? -1);

            // Broadcast to all connected clients via SignalR
            if (transaction.RiskAnalysis != null)
            {
                var notification = new TransactionNotification(
                    transaction.Id,
                    transaction.ExternalId,
                    transaction.Amount,
                    transaction.SourceCountry,
                    transaction.DestinationCountry,
                    transaction.RiskAnalysis.RiskScore,
                    transaction.RiskAnalysis.RiskLevel,
                    transaction.RiskAnalysis.ReviewStatus,
                    transaction.RiskAnalysis.Explanation ?? "Risk analysis complete"
                );
                
                await _hubContext.Clients.All.SendAsync("NewTransaction", notification, cancellationToken);
                _logger.LogInformation("Broadcasted transaction {TransactionId} to SignalR clients", transaction.Id);
            }

            return Ok(new 
            { 
                success = true, 
                transactionId = transaction.Id,
                riskScore = transaction.RiskAnalysis?.RiskScore,
                riskLevel = transaction.RiskAnalysis?.RiskLevel.ToString(),
                reviewStatus = transaction.RiskAnalysis?.ReviewStatus.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook");
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Health check endpoint for webhook configuration verification.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new 
        { 
            status = "healthy", 
            service = "PayGuard AI",
            timestamp = DateTime.UtcNow 
        });
    }
}
