namespace PayGuardAI.Core.Entities;

/// <summary>
/// Tracks a tenant's active Stripe subscription and usage for the current billing period.
/// </summary>
public class TenantSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The tenant this subscription belongs to.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Stripe Customer ID (cus_xxx)</summary>
    public string StripeCustomerId { get; set; } = string.Empty;

    /// <summary>Stripe Subscription ID (sub_xxx). Null if on free trial with no payment method.</summary>
    public string? StripeSubscriptionId { get; set; }

    /// <summary>Current plan: Starter, Pro, Enterprise, Trial</summary>
    public BillingPlan Plan { get; set; } = BillingPlan.Trial;

    /// <summary>Current billing period status mirroring Stripe: active, trialing, past_due, canceled</summary>
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
