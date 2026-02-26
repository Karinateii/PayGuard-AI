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
    private readonly ITenantContext _tenantContext;
    private readonly IWebhookDeliveryService _webhookDelivery;

    public WebhooksController(
        ITransactionService transactionService,
        IHubContext<TransactionHub> hubContext,
        IPaymentProviderFactory providerFactory,
        ILogger<WebhooksController> logger,
        IMetricsService metrics,
        ITenantContext tenantContext,
        IWebhookDeliveryService webhookDelivery)
    {
        _transactionService = transactionService;
        _hubContext = hubContext;
        _providerFactory = providerFactory;
        _logger = logger;
        _metrics = metrics;
        _tenantContext = tenantContext;
        _webhookDelivery = webhookDelivery;
    }

    /// <summary>
    /// Receives transaction webhooks from Afriex.
    /// Supports both TRANSACTION.CREATED and TRANSACTION.UPDATED events.
    /// Endpoint: POST /api/webhooks/afriex
    /// </summary>
    /// <remarks>
    /// Requires `X-Afriex-Signature` header with HMAC-SHA256 signature of the request body.
    /// 
    /// Sample Afriex webhook payload:
    /// 
    ///     POST /api/webhooks/afriex
    ///     X-Afriex-Signature: sha256=abc123...
    ///     
    ///     {
    ///       "event": "transaction.completed",
    ///       "data": {
    ///         "id": "txn-001",
    ///         "type": "send",
    ///         "status": "completed",
    ///         "amount": 500,
    ///         "currency": "USD",
    ///         "source_country": "US",
    ///         "destination_country": "NG",
    ///         "customer": { "id": "cust-1", "email": "user@example.com" }
    ///       }
    ///     }
    /// </remarks>
    /// <response code="200">Transaction processed successfully with risk analysis</response>
    /// <response code="401">Missing or invalid webhook signature</response>
    /// <response code="400">Malformed payload or processing error</response>
    /// <response code="429">Rate limit exceeded</response>
    [HttpPost("afriex")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ReceiveAfriexWebhook(CancellationToken cancellationToken)
    {
        return await ProcessWebhook("afriex", cancellationToken);
    }

    /// <summary>
    /// Receives transaction webhooks from Flutterwave.
    /// Endpoint: POST /api/webhooks/flutterwave
    /// </summary>
    /// <remarks>
    /// Requires `verif-hash` header matching your configured Flutterwave webhook secret.
    /// </remarks>
    /// <response code="200">Transaction processed successfully</response>
    /// <response code="401">Missing or invalid signature</response>
    /// <response code="400">Processing error</response>
    [HttpPost("flutterwave")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReceiveFlutterwaveWebhook(CancellationToken cancellationToken)
    {
        return await ProcessWebhook("flutterwave", cancellationToken);
    }

    /// <summary>
    /// Receives transaction webhooks from Wise (TransferWise).
    /// Wise sends transfer state change events with RSA-SHA256 signed payloads.
    /// Endpoint: POST /api/webhooks/wise
    /// </summary>
    /// <remarks>
    /// Requires `X-Signature-SHA256` header with RSA-SHA256 signature.
    /// </remarks>
    /// <response code="200">Transaction processed successfully</response>
    /// <response code="401">Missing or invalid signature</response>
    /// <response code="400">Processing error</response>
    [HttpPost("wise")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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
            _logger.LogInformation("[{Provider}] Received webhook ({PayloadLength} bytes)",
                providerName, payload.Length);

            // Verify webhook signature — MANDATORY for all providers.
            // Reject if no signature header is present (prevents unsigned payload injection).
            var signature = Request.Headers["x-webhook-signature"].FirstOrDefault()
                           ?? Request.Headers["verif-hash"].FirstOrDefault()       // Flutterwave uses verif-hash
                           ?? Request.Headers["X-Signature-SHA256"].FirstOrDefault(); // Wise uses X-Signature-SHA256

            if (string.IsNullOrEmpty(signature))
            {
                _logger.LogWarning("[{Provider}] Webhook rejected — no signature header present", providerName);
                return Unauthorized(new { success = false, error = "Missing webhook signature" });
            }

            if (!provider.VerifyWebhookSignature(payload, signature))
            {
                _logger.LogWarning("[{Provider}] Webhook signature verification failed", providerName);
                return Unauthorized(new { success = false, error = "Invalid signature" });
            }
            _logger.LogInformation("[{Provider}] Webhook signature verified successfully", providerName);

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

                var groupName = $"tenant-{_tenantContext.TenantId}";
                await _hubContext.Clients.Group(groupName).SendAsync("NewTransaction", notification, cancellationToken);
                _logger.LogInformation("[{Provider}] Broadcasted transaction {TransactionId} to tenant group {Group}",
                    providerName, transaction.Id, groupName);
            }

            // Deliver outbound webhooks to customer-configured endpoints (fire-and-forget)
            _ = DeliverOutboundWebhookAsync(transaction, cancellationToken);

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
            return BadRequest(new { success = false, error = "Webhook processing failed" });
        }
    }

    /// <summary>
    /// Health check endpoint for webhook configuration verification.
    /// SECURITY: Only returns basic status — does NOT expose provider configuration details.
    /// </summary>
    /// <response code="200">Service is healthy</response>
    [HttpGet("health")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "healthy",
            service = "PayGuard AI",
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Simulate a transaction for demo purposes.
    /// Creates a webhook payload locally and processes it through the full risk pipeline.
    /// No external API calls required.
    /// </summary>
    /// <param name="request">Transaction simulation parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Risk analysis result including score, level, and explanation</returns>
    /// <remarks>
    /// Requires authentication. The simulated transaction goes through the full pipeline:
    /// 1. Rule-based risk scoring (6 configurable rules)
    /// 2. ML model scoring (if trained model available)
    /// 3. Risk level assignment (Low/Medium/High/Critical)
    /// 4. Auto-approve or queue for HITL review
    /// 5. Real-time SignalR notification broadcast
    /// 6. Outbound webhook delivery to configured endpoints
    /// 
    /// Sample request:
    /// 
    ///     POST /api/webhooks/simulate
    ///     {
    ///       "amount": 50000,
    ///       "sourceCountry": "US",
    ///       "destinationCountry": "NG",
    ///       "transactionType": "REMITTANCE"
    ///     }
    /// </remarks>
    /// <response code="200">Transaction simulated and risk-scored successfully</response>
    /// <response code="400">Invalid request or processing error</response>
    /// <response code="401">Not authenticated</response>
    [HttpPost("simulate")]
    [Authorize]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
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

                var groupName = $"tenant-{_tenantContext.TenantId}";
                await _hubContext.Clients.Group(groupName).SendAsync("NewTransaction", notification, cancellationToken);
            }

            // Deliver outbound webhooks to customer-configured endpoints (fire-and-forget)
            _ = DeliverOutboundWebhookAsync(transaction, cancellationToken);

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
            return BadRequest(new { success = false, error = "Transaction simulation failed" });
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

    /// <summary>
    /// Fire-and-forget delivery of outbound webhook to customer endpoints.
    /// Never blocks the API response; failures are logged.
    /// </summary>
    private async Task DeliverOutboundWebhookAsync(
        PayGuardAI.Core.Entities.Transaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            var eventPayload = new
            {
                transactionId = transaction.Id,
                externalId = transaction.ExternalId,
                amount = transaction.Amount,
                sourceCurrency = transaction.SourceCurrency,
                destinationCurrency = transaction.DestinationCurrency,
                sourceCountry = transaction.SourceCountry,
                destinationCountry = transaction.DestinationCountry,
                riskScore = transaction.RiskAnalysis?.RiskScore,
                riskLevel = transaction.RiskAnalysis?.RiskLevel.ToString(),
                reviewStatus = transaction.RiskAnalysis?.ReviewStatus.ToString(),
                explanation = transaction.RiskAnalysis?.Explanation,
                analyzedAt = transaction.RiskAnalysis?.AnalyzedAt
            };

            await _webhookDelivery.DeliverEventAsync(
                _tenantContext.TenantId,
                "transaction.analyzed",
                eventPayload,
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Outbound webhooks must never crash the main pipeline
            _logger.LogWarning(ex, "Outbound webhook delivery failed for transaction {TransactionId}",
                transaction.Id);
        }
    }
}

/// <summary>
/// Request model for simulating a transaction through the risk scoring pipeline.
/// </summary>
public class SimulateTransactionRequest
{
    /// <summary>Transaction amount in source currency (e.g., 100.00)</summary>
    /// <example>5000</example>
    public decimal Amount { get; set; } = 100;

    /// <summary>ISO 3166-1 alpha-2 source country code (e.g., "US", "GB", "NG")</summary>
    /// <example>US</example>
    public string SourceCountry { get; set; } = "US";

    /// <summary>ISO 3166-1 alpha-2 destination country code</summary>
    /// <example>NG</example>
    public string DestinationCountry { get; set; } = "NG";

    /// <summary>ISO 4217 source currency code (auto-detected from country if omitted)</summary>
    /// <example>USD</example>
    public string? SourceCurrency { get; set; }

    /// <summary>Sender identifier (auto-generated if omitted)</summary>
    public string? SenderId { get; set; }

    /// <summary>Receiver identifier (auto-generated if omitted)</summary>
    public string? ReceiverId { get; set; }

    /// <summary>Transaction type: REMITTANCE, PAYMENT, DEPOSIT, WITHDRAWAL</summary>
    /// <example>REMITTANCE</example>
    public string? TransactionType { get; set; }
}