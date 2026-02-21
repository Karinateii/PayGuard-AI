using PayGuardAI.Core.Entities;

namespace PayGuardAI.Core.Services;

/// <summary>
/// Stripe billing service. Handles subscriptions, checkout, customer portal, usage metering,
/// and incoming Stripe webhook events.
/// </summary>
public interface IStripeService
{
    /// <summary>
    /// Create a Stripe Checkout Session for a tenant to subscribe to a plan.
    /// Returns the Checkout URL to redirect the user to.
    /// </summary>
    Task<string> CreateCheckoutSessionAsync(string tenantId, string email, BillingPlan plan,
        string successUrl, string cancelUrl, CancellationToken ct = default);

    /// <summary>
    /// Create a Stripe Customer Portal session so a tenant can manage/cancel their subscription.
    /// Returns the portal URL.
    /// </summary>
    Task<string> CreatePortalSessionAsync(string tenantId, string returnUrl, CancellationToken ct = default);

    /// <summary>
    /// Get the current subscription for a tenant. Returns null if none exists yet.
    /// </summary>
    Task<TenantSubscription?> GetSubscriptionAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Increment transaction usage for the current billing period.
    /// Returns true if within the plan limit, false if overage applies.
    /// </summary>
    Task<bool> RecordTransactionUsageAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Process a raw Stripe webhook event payload. Handles:
    ///   - checkout.session.completed → activate subscription
    ///   - customer.subscription.updated → sync plan/status
    ///   - customer.subscription.deleted → mark canceled
    ///   - invoice.payment_failed → send Slack alert
    /// </summary>
    Task HandleWebhookAsync(string payload, string stripeSignature, CancellationToken ct = default);

    /// <summary>
    /// Get pricing information for all plans (prices come from config, not Stripe API).
    /// </summary>
    IReadOnlyList<PlanInfo> GetPlans();
}

public record PlanInfo(
    BillingPlan Plan,
    string Name,
    string Price,
    string Description,
    int IncludedTransactions,
    string[] Features,
    string StripePriceId,
    bool IsPopular
);
