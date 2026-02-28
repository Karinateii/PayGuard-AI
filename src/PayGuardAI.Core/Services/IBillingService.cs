using PayGuardAI.Core.Entities;

namespace PayGuardAI.Core.Services;

/// <summary>
/// Payment-provider-agnostic billing service.
/// Handles subscriptions, checkout, subscription management, usage metering,
/// and incoming webhook events.
/// Implementations: PaystackBillingService (Africa), FlutterwaveBillingService (International).
/// Use BillingServiceFactory to resolve the correct provider.
/// </summary>
public interface IBillingService
{
    /// <summary>
    /// Initialize a payment transaction that subscribes the tenant to a plan.
    /// Returns the checkout/authorization URL to redirect the user to.
    /// </summary>
    Task<string> CreateCheckoutSessionAsync(string tenantId, string email, BillingPlan plan,
        string callbackUrl, CancellationToken ct = default);

    /// <summary>
    /// Get a URL where the customer can manage/update their card on a subscription.
    /// Returns the management URL.
    /// </summary>
    Task<string> GetManageSubscriptionUrlAsync(string tenantId, CancellationToken ct = default);

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
    /// Process a raw webhook event payload. Verifies signature and handles:
    ///   - subscription.create → activate subscription
    ///   - invoice.update (charge.success) → renew period
    ///   - subscription.disable → mark canceled
    ///   - invoice.payment_failed → send alert
    /// </summary>
    Task HandleWebhookAsync(string payload, string signature, CancellationToken ct = default);

    /// <summary>
    /// Verify a checkout transaction by its reference and activate the subscription.
    /// Called when the user is redirected back from the payment gateway.
    /// Returns the updated subscription, or null if verification failed.
    /// </summary>
    Task<TenantSubscription?> VerifyCheckoutAsync(string tenantId, string reference, CancellationToken ct = default);

    /// <summary>
    /// Cancel the tenant's current Paystack/Flutterwave subscription.
    /// Used before upgrading/downgrading to a different plan.
    /// </summary>
    Task CancelSubscriptionAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Get pricing information for all plans.
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
    string PlanCode,
    bool IsPopular
);
