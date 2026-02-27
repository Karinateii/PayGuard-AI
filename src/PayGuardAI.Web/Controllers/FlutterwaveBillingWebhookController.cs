using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using PayGuardAI.Data.Services;

namespace PayGuardAI.Web.Controllers;

/// <summary>
/// Receives Flutterwave billing webhook events (subscription charges, cancellations, failures).
/// Endpoint: POST /api/v1/webhooks/flutterwave-billing
///
/// IMPORTANT: This is separate from the transaction webhook at /api/v1/webhooks/flutterwave
/// which handles payment provider transaction events (Afriex/Flutterwave/Wise).
/// This controller handles BILLING events (subscription lifecycle).
///
/// Flutterwave verifies webhooks via the verif-hash header matching your configured secret hash.
/// Docs: https://developer.flutterwave.com/docs/integration-guides/webhooks
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/webhooks")]
[Route("api/webhooks")] // Backward-compatible unversioned route
[AllowAnonymous]
public class FlutterwaveBillingWebhookController : ControllerBase
{
    private readonly BillingServiceFactory _billingFactory;
    private readonly ILogger<FlutterwaveBillingWebhookController> _logger;

    public FlutterwaveBillingWebhookController(BillingServiceFactory billingFactory, ILogger<FlutterwaveBillingWebhookController> logger)
    {
        _billingFactory = billingFactory;
        _logger = logger;
    }

    /// <summary>
    /// Receives Flutterwave billing webhook events (subscription charges, cancellations, failures).
    /// </summary>
    /// <remarks>
    /// Requires `verif-hash` header matching the configured webhook secret.
    /// This is separate from the transaction webhook at `/api/webhooks/flutterwave`.
    /// </remarks>
    /// <response code="200">Webhook processed successfully</response>
    /// <response code="400">Missing signature or invalid payload</response>
    /// <response code="500">Internal processing error</response>
    [HttpPost("flutterwave-billing")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ReceiveFlutterwaveBillingWebhook(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);

        // Flutterwave sends the secret hash in the verif-hash header
        var signature = Request.Headers["verif-hash"].FirstOrDefault() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(signature))
        {
            _logger.LogWarning("Flutterwave billing webhook received with no verif-hash header");
            return BadRequest("Missing verif-hash header");
        }

        try
        {
            var flutterwaveBilling = _billingFactory.GetFlutterwave();
            await flutterwaveBilling.HandleWebhookAsync(payload, signature, cancellationToken);
            return Ok();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("signature"))
        {
            _logger.LogWarning("Flutterwave billing webhook signature invalid: {Message}", ex.Message);
            return BadRequest("Invalid Flutterwave signature");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Flutterwave billing webhook");
            return StatusCode(500);
        }
    }
}
