using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayGuardAI.Data.Services;

namespace PayGuardAI.Web.Controllers;

/// <summary>
/// Receives Paystack webhook events.
/// Endpoint: POST /api/webhooks/paystack
/// Must be excluded from CSRF + auth — Paystack posts directly to this endpoint.
///
/// Paystack verifies via HMAC SHA512 signature in the x-paystack-signature header.
/// Only accepts webhooks from Paystack IPs: 52.31.139.75, 52.49.173.169, 52.214.14.220
/// </summary>
[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
public class PaystackWebhookController : ControllerBase
{
    private readonly BillingServiceFactory _billingFactory;
    private readonly ILogger<PaystackWebhookController> _logger;

    public PaystackWebhookController(BillingServiceFactory billingFactory, ILogger<PaystackWebhookController> logger)
    {
        _billingFactory = billingFactory;
        _logger = logger;
    }

    [HttpPost("paystack")]
    public async Task<IActionResult> ReceivePaystackWebhook(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var signature = Request.Headers["x-paystack-signature"].FirstOrDefault() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(signature))
        {
            _logger.LogWarning("Paystack webhook received with no x-paystack-signature header");
            return BadRequest("Missing x-paystack-signature header");
        }

        try
        {
            var paystackBilling = _billingFactory.GetPaystack();
            await paystackBilling.HandleWebhookAsync(payload, signature, cancellationToken);
            return Ok();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("signature"))
        {
            _logger.LogWarning("Paystack webhook signature invalid: {Message}", ex.Message);
            return BadRequest("Invalid Paystack signature");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Paystack webhook");
            return StatusCode(500);
        }
    }
}
