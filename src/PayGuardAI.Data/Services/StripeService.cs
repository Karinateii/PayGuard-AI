using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;
using Stripe;
using Stripe.Checkout;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Stripe billing implementation.
/// Feature flag: FeatureFlags:BillingEnabled
///
/// Stripe config keys (set in Railway env vars for production):
///   Stripe:SecretKey        — sk_live_xxx  (sk_test_xxx for testing)
///   Stripe:WebhookSecret    — whsec_xxx
///   Stripe:StarterPriceId   — price_xxx
///   Stripe:ProPriceId       — price_xxx
///   Stripe:EnterprisePriceId — price_xxx
/// </summary>
public class StripeService : IStripeService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly IAlertingService _alerting;
    private readonly ILogger<StripeService> _logger;

    // Plan limits
    private static readonly Dictionary<BillingPlan, int> PlanLimits = new()
    {
        [BillingPlan.Trial]      = 100,
        [BillingPlan.Starter]    = 1_000,
        [BillingPlan.Pro]        = 10_000,
        [BillingPlan.Enterprise] = int.MaxValue
    };

    public StripeService(
        ApplicationDbContext db,
        IConfiguration config,
        IAlertingService alerting,
        ILogger<StripeService> logger)
    {
        _db = db;
        _config = config;
        _alerting = alerting;
        _logger = logger;

        StripeConfiguration.ApiKey = _config["Stripe:SecretKey"] ?? string.Empty;
    }

    // ── Checkout ──────────────────────────────────────────────────────────────

    public async Task<string> CreateCheckoutSessionAsync(
        string tenantId, string email, BillingPlan plan,
        string successUrl, string cancelUrl, CancellationToken ct = default)
    {
        var priceId = GetPriceId(plan);
        if (string.IsNullOrWhiteSpace(priceId))
            throw new InvalidOperationException($"No Stripe price ID configured for plan {plan}. Set Stripe:{plan}PriceId in config.");

        // Reuse existing Stripe customer if we have one
        var sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        var customerId = sub?.StripeCustomerId;

        if (string.IsNullOrWhiteSpace(customerId))
        {
            var customerService = new CustomerService();
            var customer = await customerService.CreateAsync(new CustomerCreateOptions
            {
                Email = email,
                Metadata = new Dictionary<string, string> { ["tenantId"] = tenantId }
            }, cancellationToken: ct);
            customerId = customer.Id;
        }

        var options = new SessionCreateOptions
        {
            Customer = customerId,
            Mode = "subscription",
            LineItems = [new SessionLineItemOptions { Price = priceId, Quantity = 1 }],
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                TrialPeriodDays = sub == null ? 14 : null,  // only offer trial to new customers
                Metadata = new Dictionary<string, string> { ["tenantId"] = tenantId }
            },
            Metadata = new Dictionary<string, string> { ["tenantId"] = tenantId },
            SuccessUrl = successUrl + "?session_id={CHECKOUT_SESSION_ID}",
            CancelUrl = cancelUrl,
            AllowPromotionCodes = true,
        };

        var sessionService = new SessionService();
        var session = await sessionService.CreateAsync(options, cancellationToken: ct);

        _logger.LogInformation("Created Stripe checkout session {SessionId} for tenant {TenantId}, plan {Plan}",
            session.Id, tenantId, plan);

        return session.Url;
    }

    // ── Customer Portal ───────────────────────────────────────────────────────

    public async Task<string> CreatePortalSessionAsync(string tenantId, string returnUrl, CancellationToken ct = default)
    {
        var sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"No subscription found for tenant {tenantId}");

        if (string.IsNullOrWhiteSpace(sub.StripeCustomerId))
            throw new InvalidOperationException("Tenant has no Stripe customer ID — they need to complete checkout first.");

        var portalService = new Stripe.BillingPortal.SessionService();
        var session = await portalService.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = sub.StripeCustomerId,
            ReturnUrl = returnUrl
        }, cancellationToken: ct);

        _logger.LogInformation("Created Stripe portal session for tenant {TenantId}", tenantId);
        return session.Url;
    }

    // ── Subscription Query ────────────────────────────────────────────────────

    public async Task<TenantSubscription?> GetSubscriptionAsync(string tenantId, CancellationToken ct = default)
        => await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

    // ── Usage Metering ────────────────────────────────────────────────────────

    public async Task<bool> RecordTransactionUsageAsync(string tenantId, CancellationToken ct = default)
    {
        var sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (sub == null) return true; // Billing not yet set up — allow through

        // Reset counter if we've entered a new billing period
        if (DateTime.UtcNow > sub.PeriodEnd)
        {
            sub.TransactionsThisPeriod = 0;
            sub.PeriodStart = sub.PeriodEnd;
            sub.PeriodEnd = sub.PeriodEnd.AddMonths(1);
        }

        sub.TransactionsThisPeriod++;
        sub.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var limit = PlanLimits.GetValueOrDefault(sub.Plan, 1000);
        var isOverage = sub.Plan != BillingPlan.Enterprise && sub.TransactionsThisPeriod > limit;

        if (isOverage && sub.TransactionsThisPeriod % 100 == 0)
        {
            // Alert every 100 transactions over limit to avoid spam
            await _alerting.AlertAsync(
                $"⚠️ Tenant {tenantId} is {sub.TransactionsThisPeriod - limit} transactions over their {sub.Plan} plan limit ({limit}/mo). " +
                $"Consider upgrading or overage billing will apply.", ct);
        }

        return !isOverage;
    }

    // ── Webhook Handler ───────────────────────────────────────────────────────

    public async Task HandleWebhookAsync(string payload, string stripeSignature, CancellationToken ct = default)
    {
        var webhookSecret = _config["Stripe:WebhookSecret"] ?? string.Empty;

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, stripeSignature, webhookSecret);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("Stripe webhook signature validation failed: {Message}", ex.Message);
            throw;
        }

        _logger.LogInformation("Processing Stripe webhook event: {EventType} ({EventId})", stripeEvent.Type, stripeEvent.Id);

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                await HandleCheckoutCompletedAsync((Session)stripeEvent.Data.Object, ct);
                break;

            case "customer.subscription.updated":
                await HandleSubscriptionUpdatedAsync((Subscription)stripeEvent.Data.Object, ct);
                break;

            case "customer.subscription.deleted":
                await HandleSubscriptionDeletedAsync((Subscription)stripeEvent.Data.Object, ct);
                break;

            case "invoice.payment_failed":
                await HandlePaymentFailedAsync((Invoice)stripeEvent.Data.Object, ct);
                break;

            case "invoice.payment_succeeded":
                _logger.LogInformation("Payment succeeded for customer {CustomerId}", 
                    ((Invoice)stripeEvent.Data.Object).CustomerId);
                break;

            default:
                _logger.LogDebug("Unhandled Stripe event type: {EventType}", stripeEvent.Type);
                break;
        }
    }

    // ── Plan Info (no Stripe API call needed) ─────────────────────────────────

    public IReadOnlyList<PlanInfo> GetPlans() =>
    [
        new(BillingPlan.Starter,
            "Starter",
            "$99/mo",
            "For small teams processing up to 1,000 transactions/month",
            1_000,
            ["Up to 1,000 transactions/month", "Real-time risk scoring", "HITL review queue",
             "Flutterwave + Afriex webhooks", "Email alerts", "5 team members"],
            _config["Stripe:StarterPriceId"] ?? "",
            false),

        new(BillingPlan.Pro,
            "Pro",
            "$499/mo",
            "For growing fintechs processing up to 10,000 transactions/month",
            10_000,
            ["Up to 10,000 transactions/month", "Everything in Starter",
             "Slack real-time alerts", "Advanced risk rules", "Compliance reports",
             "Priority support", "25 team members"],
            _config["Stripe:ProPriceId"] ?? "",
            true),

        new(BillingPlan.Enterprise,
            "Enterprise",
            "$2,000/mo",
            "For high-volume teams with unlimited transactions and dedicated support",
            int.MaxValue,
            ["Unlimited transactions", "Everything in Pro",
             "Custom risk rules", "SOC 2 audit logs", "Dedicated Slack channel",
             "SLA guarantee", "Unlimited team members", "Custom integrations"],
            _config["Stripe:EnterprisePriceId"] ?? "",
            false)
    ];

    // ── Private Helpers ───────────────────────────────────────────────────────

    private async Task HandleCheckoutCompletedAsync(Session session, CancellationToken ct)
    {
        var tenantId = session.Metadata?.GetValueOrDefault("tenantId") ?? session.ClientReferenceId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogWarning("checkout.session.completed missing tenantId metadata on session {SessionId}", session.Id);
            return;
        }

        // Retrieve full subscription from Stripe to get plan details
        var subscriptionService = new SubscriptionService();
        var stripeSub = await subscriptionService.GetAsync(session.SubscriptionId, cancellationToken: ct);
        var plan = MapStripePriceToPlan(stripeSub.Items.Data.FirstOrDefault()?.Price?.Id);

        var existing = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (existing == null)
        {
            existing = new TenantSubscription { TenantId = tenantId };
            _db.TenantSubscriptions.Add(existing);
        }

        // CurrentPeriodStart/End moved to SubscriptionItem in Stripe.net 50.x
        var checkoutFirstItem = stripeSub.Items.Data.FirstOrDefault();

        existing.StripeCustomerId = session.CustomerId;
        existing.StripeSubscriptionId = session.SubscriptionId;
        existing.Plan = plan;
        existing.Status = stripeSub.Status;
        existing.IncludedTransactions = PlanLimits[plan];
        existing.PeriodStart = checkoutFirstItem?.CurrentPeriodStart ?? DateTime.UtcNow;
        existing.PeriodEnd = checkoutFirstItem?.CurrentPeriodEnd ?? DateTime.UtcNow.AddMonths(1);
        existing.TrialEndsAt = stripeSub.TrialEnd;
        existing.BillingEmail = session.CustomerEmail ?? string.Empty;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Tenant {TenantId} subscribed to {Plan} plan via checkout session {SessionId}",
            tenantId, plan, session.Id);
    }

    private async Task HandleSubscriptionUpdatedAsync(Subscription stripeSub, CancellationToken ct)
    {
        var tenantId = stripeSub.Metadata?.GetValueOrDefault("tenantId") ?? string.Empty;
        var sub = await FindSubByStripeIdAsync(stripeSub.Id, tenantId, ct);
        if (sub == null) return;

        var updatedFirstItem = stripeSub.Items.Data.FirstOrDefault();
        var plan = MapStripePriceToPlan(updatedFirstItem?.Price?.Id);
        sub.Plan = plan;
        sub.Status = stripeSub.Status;
        sub.IncludedTransactions = PlanLimits[plan];
        // CurrentPeriodStart/End moved to SubscriptionItem in Stripe.net 50.x
        sub.PeriodStart = updatedFirstItem?.CurrentPeriodStart ?? DateTime.UtcNow;
        sub.PeriodEnd = updatedFirstItem?.CurrentPeriodEnd ?? DateTime.UtcNow.AddMonths(1);
        sub.TrialEndsAt = stripeSub.TrialEnd;
        sub.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Subscription updated for tenant {TenantId}: Plan={Plan}, Status={Status}",
            sub.TenantId, plan, stripeSub.Status);
    }

    private async Task HandleSubscriptionDeletedAsync(Subscription stripeSub, CancellationToken ct)
    {
        var tenantId = stripeSub.Metadata?.GetValueOrDefault("tenantId") ?? string.Empty;
        var sub = await FindSubByStripeIdAsync(stripeSub.Id, tenantId, ct);
        if (sub == null) return;

        sub.Status = "canceled";
        sub.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _alerting.AlertAsync($"🔴 Tenant {sub.TenantId} canceled their {sub.Plan} subscription.", ct);
        _logger.LogWarning("Subscription canceled for tenant {TenantId}", sub.TenantId);
    }

    private async Task HandlePaymentFailedAsync(Invoice invoice, CancellationToken ct)
    {
        var sub = await _db.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.StripeCustomerId == invoice.CustomerId, ct);

        var tenantId = sub?.TenantId ?? invoice.CustomerId;
        var amount = (invoice.AmountDue / 100m).ToString("C");

        await _alerting.AlertAsync(
            $"❌ Payment failed for tenant {tenantId} — {amount} due. " +
            $"Invoice: {invoice.Id}. Subscription may be suspended if not resolved.", ct);

        _logger.LogWarning("Payment failed for tenant {TenantId}, invoice {InvoiceId}, amount {Amount}",
            tenantId, invoice.Id, amount);
    }

    private async Task<TenantSubscription?> FindSubByStripeIdAsync(string stripeSubId, string tenantId, CancellationToken ct)
    {
        var sub = await _db.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubId, ct);

        if (sub == null && !string.IsNullOrWhiteSpace(tenantId))
            sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        if (sub == null)
            _logger.LogWarning("Could not find subscription for Stripe sub {StripeSubId}", stripeSubId);

        return sub;
    }

    private string GetPriceId(BillingPlan plan) => plan switch
    {
        BillingPlan.Starter    => _config["Stripe:StarterPriceId"] ?? string.Empty,
        BillingPlan.Pro        => _config["Stripe:ProPriceId"] ?? string.Empty,
        BillingPlan.Enterprise => _config["Stripe:EnterprisePriceId"] ?? string.Empty,
        _ => string.Empty
    };

    private BillingPlan MapStripePriceToPlan(string? priceId)
    {
        if (priceId == _config["Stripe:EnterprisePriceId"]) return BillingPlan.Enterprise;
        if (priceId == _config["Stripe:ProPriceId"])        return BillingPlan.Pro;
        if (priceId == _config["Stripe:StarterPriceId"])    return BillingPlan.Starter;
        return BillingPlan.Starter; // default
    }
}
