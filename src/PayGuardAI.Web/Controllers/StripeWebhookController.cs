using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Web.Controllers;

/// <summary>
/// Receives Stripe webhook events.
/// Endpoint: POST /api/webhooks/stripe
/// Must be excluded from CSRF + auth — Stripe posts directly to this endpoint.
/// </summary>
[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
public class StripeWebhookController : ControllerBase
{
    private readonly IStripeService _stripeService;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(IStripeService stripeService, ILogger<StripeWebhookController> logger)
    {
        _stripeService = stripeService;
        _logger = logger;
    }

    [HttpPost("stripe")]
    public async Task<IActionResult> ReceiveStripeWebhook(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var signature = Request.Headers["Stripe-Signature"].FirstOrDefault() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(signature))
        {
            _logger.LogWarning("Stripe webhook received with no Stripe-Signature header");
            return BadRequest("Missing Stripe-Signature header");
        }

        try
        {
            await _stripeService.HandleWebhookAsync(payload, signature, cancellationToken);
            return Ok();
        }
        catch (Stripe.StripeException ex)
        {
            _logger.LogWarning("Stripe webhook signature invalid: {Message}", ex.Message);
            return BadRequest("Invalid Stripe signature");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing Stripe webhook");
            return StatusCode(500);
        }
    }
}
