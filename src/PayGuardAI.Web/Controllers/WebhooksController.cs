using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using PayGuardAI.Core.Services;
using PayGuardAI.Data.Services;
using PayGuardAI.Web.Hubs;

namespace PayGuardAI.Web.Controllers;

/// <summary>
/// Webhook controller for receiving payment provider events.
/// Supports multiple providers: Afriex, Flutterwave, Wise, etc.
/// Per-API-key rate limiting applied for programmatic access control.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("PerApiKey")]
public class WebhooksController : ControllerBase
{
    private readonly ITransactionService _transactionService;
    private readonly IHubContext<TransactionHub> _hubContext;
    private readonly IPaymentProviderFactory _providerFactory;
    private readonly ILogger<WebhooksController> _logger;
    private readonly IMetricsService _metrics;

    public WebhooksController(
        ITransactionService transactionService,
        IHubContext<TransactionHub> hubContext,
        IPaymentProviderFactory providerFactory,
        ILogger<WebhooksController> logger,
        IMetricsService metrics)
    {
        _transactionService = transactionService;
        _hubContext = hubContext;
        _providerFactory = providerFactory;
        _logger = logger;
        _metrics = metrics;
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
    /// Receives transaction webhooks from Wise (TransferWise).
    /// Wise sends transfer state change events with RSA-SHA256 signed payloads.
    /// Endpoint: POST /api/webhooks/wise
    /// </summary>
    [HttpPost("wise")]
    public async Task<IActionResult> ReceiveWiseWebhook(CancellationToken cancellationToken)
    {
        return await ProcessWebhook("wise", cancellationToken);
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

            _metrics.RecordWebhookReceived(providerName);
            _logger.LogInformation("[{Provider}] Received webhook: {PayloadPreview}...",
                providerName, payload.Length > 100 ? payload[..100] : payload);

            // Verify signature based on provider
            var signature = Request.Headers["x-webhook-signature"].FirstOrDefault()
                           ?? Request.Headers["verif-hash"].FirstOrDefault()       // Flutterwave uses verif-hash
                           ?? Request.Headers["X-Signature-SHA256"].FirstOrDefault(); // Wise uses X-Signature-SHA256

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

    /// <summary>
    /// Simulate a transaction for demo purposes.
    /// Creates a webhook payload locally and processes it through the full risk pipeline.
    /// No external API calls required.
    /// </summary>
    [HttpPost("simulate")]
    [Authorize]
    public async Task<IActionResult> SimulateTransaction(
        [FromBody] SimulateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("[SIMULATE] Simulating transaction: {Amount} {SourceCurrency} from {SourceCountry} to {DestCountry}",
                request.Amount, request.SourceCurrency, request.SourceCountry, request.DestinationCountry);

            var transactionId = Guid.NewGuid().ToString();
            var destCurrency = GetCurrencyForCountry(request.DestinationCountry);
            var srcCurrency = request.SourceCurrency ?? "USD";

            var payload = $$"""
            {
                "event": "transaction.completed",
                "data": {
                    "id": "{{transactionId}}",
                    "type": "{{request.TransactionType ?? "REMITTANCE"}}",
                    "status": "COMPLETED",
                    "amount": {{request.Amount}},
                    "sourceCurrency": "{{srcCurrency}}",
                    "destinationCurrency": "{{destCurrency}}",
                    "senderId": "{{request.SenderId ?? $"sender-{Guid.NewGuid().ToString()[..8]}"}}",
                    "receiverId": "{{request.ReceiverId ?? $"receiver-{Guid.NewGuid().ToString()[..8]}"}}",
                    "sourceCountry": "{{request.SourceCountry}}",
                    "destinationCountry": "{{request.DestinationCountry}}",
                    "createdAt": "{{DateTime.UtcNow:O}}"
                }
            }
            """;

            var transaction = await _transactionService.ProcessWebhookAsync(payload, cancellationToken);

            _logger.LogInformation("[SIMULATE] Transaction {Id} processed - Risk: {Score} ({Level})",
                transaction.Id, transaction.RiskAnalysis?.RiskScore, transaction.RiskAnalysis?.RiskLevel);

            // Broadcast to SignalR
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
            }

            return Ok(new
            {
                success = true,
                transactionId = transaction.Id,
                externalId = transactionId,
                amount = transaction.Amount,
                sourceCountry = transaction.SourceCountry,
                destinationCountry = transaction.DestinationCountry,
                riskScore = transaction.RiskAnalysis?.RiskScore,
                riskLevel = transaction.RiskAnalysis?.RiskLevel.ToString(),
                reviewStatus = transaction.RiskAnalysis?.ReviewStatus.ToString(),
                explanation = transaction.RiskAnalysis?.Explanation
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SIMULATE] Error simulating transaction");
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    private static string GetCurrencyForCountry(string country) => country switch
    {
        "NG" => "NGN",
        "US" => "USD",
        "GB" => "GBP",
        "KE" => "KES",
        "GH" => "GHS",
        "ZA" => "ZAR",
        "CA" => "CAD",
        "DE" => "EUR",
        "TZ" => "TZS",
        "UG" => "UGX",
        "KP" => "KPW",
        "SY" => "SYP",
        _ => "USD"
    };
}

public class SimulateTransactionRequest
{
    public decimal Amount { get; set; } = 100;
    public string SourceCountry { get; set; } = "US";
    public string DestinationCountry { get; set; } = "NG";
    public string? SourceCurrency { get; set; }
    public string? SenderId { get; set; }
    public string? ReceiverId { get; set; }
    public string? TransactionType { get; set; }
}