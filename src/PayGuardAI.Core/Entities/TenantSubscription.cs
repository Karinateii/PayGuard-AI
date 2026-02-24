namespace PayGuardAI.Core.Entities;

/// <summary>
/// Tracks a tenant's active subscription and usage for the current billing period.
/// Provider-agnostic: works with Paystack, Stripe, or any payment provider.
/// </summary>
public class TenantSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The tenant this subscription belongs to.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Payment provider customer ID (e.g. Paystack CUS_xxx)</summary>
    public string ProviderCustomerId { get; set; } = string.Empty;

    /// <summary>Payment provider subscription code (e.g. Paystack SUB_xxx). Null if on free trial.</summary>
    public string? ProviderSubscriptionId { get; set; }

    /// <summary>Payment provider plan code (e.g. Paystack PLN_xxx)</summary>
    public string? ProviderPlanCode { get; set; }

    /// <summary>Email token required by Paystack to enable/disable subscriptions</summary>
    public string? ProviderEmailToken { get; set; }

    /// <summary>Which billing provider owns this subscription: "paystack" or "flutterwave"</summary>
    public string Provider { get; set; } = "paystack";

    /// <summary>Current plan: Starter, Pro, Enterprise, Trial</summary>
    public BillingPlan Plan { get; set; } = BillingPlan.Trial;

    /// <summary>Current billing period status: active, trialing, past_due, canceled</summary>
    public string Status { get; set; } = "trialing";

    /// <summary>Transactions processed in the current billing period.</summary>
    public int TransactionsThisPeriod { get; set; } = 0;

    /// <summary>Included transactions for this plan per period.</summary>
    public int IncludedTransactions { get; set; } = 1000;

    /// <summary>Start of the current billing period (UTC).</summary>
    public DateTime PeriodStart { get; set; } = DateTime.UtcNow;

    /// <summary>End of the current billing period (UTC).</summary>
    public DateTime PeriodEnd { get; set; } = DateTime.UtcNow.AddDays(14); // 14-day trial default

    /// <summary>Trial ends at (UTC). Null if not on trial.</summary>
    public DateTime? TrialEndsAt { get; set; } = DateTime.UtcNow.AddDays(14);

    /// <summary>Email address for billing notifications.</summary>
    public string BillingEmail { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum BillingPlan
{
    Trial = 0,
    Starter = 1,   // $99/mo — 1,000 transactions
    Pro = 2,        // $499/mo — 10,000 transactions
    Enterprise = 3  // $2,000/mo — unlimited
}
