using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PayGuardAI.Core.Services;
using PayGuardAI.Data.Services;
using PayGuardAI.Web.Hubs;

namespace PayGuardAI.Web.Controllers;

/// <summary>
/// Webhook controller for receiving payment provider events.
/// Supports multiple providers: Afriex, Flutterwave, Wise, etc.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class WebhooksController : ControllerBase
{
    private readonly ITransactionService _transactionService;
    private readonly IHubContext<TransactionHub> _hubContext;
    private readonly IPaymentProviderFactory _providerFactory;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        ITransactionService transactionService,
        IHubContext<TransactionHub> hubContext,
        IPaymentProviderFactory providerFactory,
        ILogger<WebhooksController> logger)
    {
        _transactionService = transactionService;
        _hubContext = hubContext;
        _providerFactory = providerFactory;
        _logger = logger;
    }

    /// <summary>
    /// Receives transaction webhooks from Afriex.
    /// Supports both TRANSACTION.CREATED and TRANSACTION.UPDATED events.
    /// Endpoint: POST /api/webhooks/afriex
    /// </summary>
    [HttpPost("afriex")]
    public async Task<IActionResult> ReceiveAfriexWebhook(CancellationToken cancellationToken)
    {
        return await ProcessWebhook("afriex", cancellationToken);
    }

    /// <summary>
    /// Receives transaction webhooks from Flutterwave.
    /// Endpoint: POST /api/webhooks/flutterwave
    /// </summary>
    [HttpPost("flutterwave")]
    public async Task<IActionResult> ReceiveFlutterwaveWebhook(CancellationToken cancellationToken)
    {
        return await ProcessWebhook("flutterwave", cancellationToken);
    }

    /// <summary>
    /// Generic transaction webhook endpoint (auto-detects provider)
    /// Legacy endpoint for backward compatibility
    /// Endpoint: POST /api/webhooks/transaction
    /// </summary>
    [HttpPost("transaction")]
    public async Task<IActionResult> ReceiveTransaction(CancellationToken cancellationToken)
    {
        // Default to Afriex for backward compatibility
        return await ProcessWebhook("afriex", cancellationToken);
    }

    private async Task<IActionResult> ProcessWebhook(string providerName, CancellationToken cancellationToken)
    {
        try
        {
            // Get the appropriate provider
            var provider = _providerFactory.GetProviderByName(providerName);

            // Read raw payload
            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync(cancellationToken);

            _logger.LogInformation("[{Provider}] Received webhook: {PayloadPreview}...",
                providerName, payload.Length > 100 ? payload[..100] : payload);

            // Verify signature based on provider
            var signature = Request.Headers["x-webhook-signature"].FirstOrDefault()
                           ?? Request.Headers["verif-hash"].FirstOrDefault(); // Flutterwave uses verif-hash

            if (!string.IsNullOrEmpty(signature))
            {
                if (!provider.VerifyWebhookSignature(payload, signature))
                {
                    _logger.LogWarning("[{Provider}] Webhook signature verification failed", providerName);
                    return Unauthorized(new { success = false, error = "Invalid signature" });
                }
                _logger.LogInformation("[{Provider}] Webhook signature verified successfully", providerName);
            }

            // Normalize the webhook to unified format
            var normalizedTransaction = await provider.NormalizeWebhookAsync(payload);

            // Process the transaction through unified service
            var transaction = await _transactionService.ProcessWebhookAsync(payload, cancellationToken);

            _logger.LogInformation("[{Provider}] Transaction {TransactionId} processed with risk score {RiskScore}",
                providerName, transaction.Id, transaction.RiskAnalysis?.RiskScore ?? -1);

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
                _logger.LogInformation("[{Provider}] Broadcasted transaction {TransactionId} to SignalR clients",
                    providerName, transaction.Id);
            }

            return Ok(new
            {
                success = true,
                provider = providerName,
                transactionId = transaction.Id,
                riskScore = transaction.RiskAnalysis?.RiskScore,
                riskLevel = transaction.RiskAnalysis?.RiskLevel.ToString(),
                reviewStatus = transaction.RiskAnalysis?.ReviewStatus.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Provider}] Error processing webhook", providerName);
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Health check endpoint for webhook configuration verification.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        var providers = _providerFactory.GetAllProviders()
            .Select(p => new { name = p.ProviderName, configured = p.IsConfigured() })
            .ToList();

        return Ok(new
        {
            status = "healthy",
            service = "PayGuard AI",
            timestamp = DateTime.UtcNow,
            providers
        });
    }
}