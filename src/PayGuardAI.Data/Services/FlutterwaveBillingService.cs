using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Flutterwave billing implementation for international customers.
/// Feature flags: FeatureFlags:BillingEnabled AND FeatureFlags:FlutterwaveBillingEnabled
///
/// Flutterwave config keys (set in Railway env vars for production):
///   FlutterwaveBilling:SecretKey        — FLWSECK_TEST-xxx (FLWSECK-xxx for live)
///   FlutterwaveBilling:PublicKey        — FLWPUBK_TEST-xxx
///   FlutterwaveBilling:WebhookSecretHash — your webhook secret hash
///   FlutterwaveBilling:StarterPlanId    — Flutterwave payment plan ID
///   FlutterwaveBilling:ProPlanId        — Flutterwave payment plan ID
///   FlutterwaveBilling:EnterprisePlanId — Flutterwave payment plan ID
///
/// Flutterwave API docs: https://developer.flutterwave.com/docs
/// Payment Plans: https://developer.flutterwave.com/docs/recurring-payments/payment-plans
/// Standard Payments: https://developer.flutterwave.com/docs/collecting-payments/standard
/// Webhooks: https://developer.flutterwave.com/docs/integration-guides/webhooks
/// </summary>
public class FlutterwaveBillingService : IBillingService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly IAlertingService _alerting;
    private readonly ILogger<FlutterwaveBillingService> _logger;
    private readonly HttpClient _http;

    private const string FlutterwaveBaseUrl = "https://api.flutterwave.com/v3";

    // Plan limits — same across all providers
    private static readonly Dictionary<BillingPlan, int> PlanLimits = new()
    {
        [BillingPlan.Trial]      = 100,
        [BillingPlan.Starter]    = 1_000,
        [BillingPlan.Pro]        = 10_000,
        [BillingPlan.Enterprise] = int.MaxValue
    };

    public FlutterwaveBillingService(
        ApplicationDbContext db,
        IConfiguration config,
        IAlertingService alerting,
        ILogger<FlutterwaveBillingService> logger,
        HttpClient http)
    {
        _db = db;
        _config = config;
        _alerting = alerting;
        _logger = logger;
        _http = http;

        _http.BaseAddress = new Uri(FlutterwaveBaseUrl);
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config["FlutterwaveBilling:SecretKey"] ?? ""}");
    }

    // ── Checkout (Flutterwave Standard Payment with Payment Plan) ─────────────

    public async Task<string> CreateCheckoutSessionAsync(
        string tenantId, string email, BillingPlan plan,
        string callbackUrl, CancellationToken ct = default)
    {
        var planId = GetPlanId(plan);
        if (string.IsNullOrWhiteSpace(planId))
            throw new InvalidOperationException($"No Flutterwave plan ID configured for plan {plan}. Set FlutterwaveBilling:{plan}PlanId in config.");

        var amount = GetPlanAmount(plan);
        var txRef = $"payguard-{tenantId}-{plan}-{DateTime.UtcNow:yyyyMMddHHmmss}";

        // Flutterwave Standard: POST /payments to create a hosted checkout link
        var payload = new
        {
            tx_ref = txRef,
            amount,
            currency = "USD",
            redirect_url = callbackUrl,
            payment_plan = planId,
            customer = new { email, name = tenantId },
            customizations = new
            {
                title = "PayGuard AI Subscription",
                description = $"PayGuard AI {plan} Plan — Monthly Subscription",
                logo = "https://payguard-ai-production.up.railway.app/payguard-logo.png"
            },
            meta = new { tenant_id = tenantId, plan = plan.ToString() }
        };

        var response = await PostAsync<FlutterwavePaymentResponse>("/payments", payload, ct);

        if (response?.Status != "success" || response.Data == null)
            throw new InvalidOperationException("Flutterwave payment initialization failed: " + (response?.Message ?? "unknown error"));

        _logger.LogInformation("Created Flutterwave checkout for tenant {TenantId}, plan {Plan}, ref {TxRef}",
            tenantId, plan, txRef);

        // Ensure we have a subscription record to link back to after webhook
        var existing = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (existing == null)
        {
            existing = new TenantSubscription
            {
                TenantId = tenantId,
                BillingEmail = email,
                Plan = BillingPlan.Trial,
                Status = "pending",
                Provider = "flutterwave",
                ProviderPlanCode = planId
            };
            _db.TenantSubscriptions.Add(existing);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            // Update provider if switching from Paystack
            existing.Provider = "flutterwave";
            existing.ProviderPlanCode = planId;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return response.Data.Link;
    }

    // ── Verify Checkout (on redirect back from Flutterwave) ──────────────────

    public async Task<TenantSubscription?> VerifyCheckoutAsync(
        string tenantId, string reference, CancellationToken ct = default)
    {
        // Flutterwave: verify via GET /transactions/:id/verify or by tx_ref
        // For now, look up the pending subscription and activate it
        // Full verification can be added when Flutterwave is enabled
        _logger.LogInformation("Flutterwave checkout verify requested for tenant {TenantId}, ref {Reference}",
            tenantId, reference);

        var sub = await _db.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        return sub; // Return current state — webhook will handle activation
    }

    // ── Cancel Subscription (for upgrade/downgrade) ──────────────────────────

    public async Task CancelSubscriptionAsync(string tenantId, CancellationToken ct = default)
    {
        var sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (sub == null) return;

        // Clear provider fields so the new checkout starts fresh
        sub.ProviderSubscriptionId = null;
        sub.ProviderPlanCode = null;
        sub.Status = "pending";
        sub.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Subscription cleared for tenant {TenantId} (ready for plan change)", tenantId);
    }

    // ── Manage Subscription ───────────────────────────────────────────────────

    public async Task<string> GetManageSubscriptionUrlAsync(string tenantId, CancellationToken ct = default)
    {
        var sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"No subscription found for tenant {tenantId}");

        // Flutterwave doesn't have a native "manage subscription" portal like Paystack.
        // We redirect to the pricing page where they can re-subscribe on a different plan,
        // or cancel via the Flutterwave dashboard link.
        if (!string.IsNullOrWhiteSpace(sub.ProviderSubscriptionId))
        {
            // If they have an active subscription, link to Flutterwave's cancel endpoint info
            return $"https://dashboard.flutterwave.com/subscriptions";
        }

        throw new InvalidOperationException("Tenant has no active subscription — they need to complete checkout first.");
    }

    // ── Subscription Query ────────────────────────────────────────────────────

    public async Task<TenantSubscription?> GetSubscriptionAsync(string tenantId, CancellationToken ct = default)
        => await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

    // ── Usage Metering (same logic as Paystack — provider-agnostic) ───────────

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
            await _alerting.AlertAsync(
                $"⚠️ Tenant {tenantId} is {sub.TransactionsThisPeriod - limit} transactions over their {sub.Plan} plan limit ({limit}/mo). " +
                $"Consider upgrading or overage billing will apply.", ct);
        }

        return !isOverage;
    }

    // ── Webhook Handler ───────────────────────────────────────────────────────

    public async Task HandleWebhookAsync(string payload, string signature, CancellationToken ct = default)
    {
        // Flutterwave verifies webhooks via the verif-hash header matching your secret hash
        var webhookSecret = _config["FlutterwaveBilling:WebhookSecretHash"] ?? string.Empty;

        if (!string.Equals(signature, webhookSecret, StringComparison.Ordinal))
        {
            _logger.LogWarning("Flutterwave billing webhook signature validation failed");
            throw new InvalidOperationException("Invalid Flutterwave billing webhook signature");
        }

        var webhookEvent = JsonSerializer.Deserialize<FlutterwaveWebhookEvent>(payload, JsonOptions);
        if (webhookEvent == null)
        {
            _logger.LogWarning("Could not deserialize Flutterwave billing webhook payload");
            return;
        }

        _logger.LogInformation("Processing Flutterwave billing webhook event: {EventType}", webhookEvent.Event);

        switch (webhookEvent.Event)
        {
            case "charge.completed":
                await HandleChargeCompletedAsync(webhookEvent, ct);
                break;

            case "subscription.cancelled":
                await HandleSubscriptionCancelledAsync(webhookEvent, ct);
                break;

            case "payment.failed":
            case "transfer.failed":
                await HandlePaymentFailedAsync(webhookEvent, ct);
                break;

            default:
                _logger.LogDebug("Unhandled Flutterwave billing event: {EventType}", webhookEvent.Event);
                break;
        }
    }

    // ── Plan Info ─────────────────────────────────────────────────────────────

    public IReadOnlyList<PlanInfo> GetPlans() =>
    [
        new(BillingPlan.Starter,
            "Starter",
            "$99/mo",
            "Essential fraud detection for small teams",
            1_000,
            ["Up to 1,000 transactions/month",
             "Real-time risk scoring",
             "6 built-in detection rules",
             "HITL review queue",
             "Transaction monitoring dashboard",
             "Basic CSV export",
             "Email alerts",
             "Watchlists (manual entry)",
             "Customer risk profiles",
             "Full audit trail",
             "2 API keys",
             "5 team members"],
            _config["FlutterwaveBilling:StarterPlanId"] ?? "",
            false),

        new(BillingPlan.Pro,
            "Pro",
            "$499/mo",
            "Advanced AI-powered protection for growing fintechs",
            10_000,
            ["Up to 10,000 transactions/month",
             "Everything in Starter",
             "Custom & compound risk rules",
             "Rule marketplace (industry templates)",
             "ML-powered risk scoring (auto-retrain)",
             "AI rule suggestions",
             "Advanced analytics & corridor heatmaps",
             "Compliance reports & CSV export",
             "Outbound webhooks (5 endpoints)",
             "Slack real-time alerts",
             "Watchlist CSV import",
             "Custom roles & permissions",
             "10 API keys",
             "25 team members"],
            _config["FlutterwaveBilling:ProPlanId"] ?? "",
            true),

        new(BillingPlan.Enterprise,
            "Enterprise",
            "$2,000/mo",
            "Unlimited protection with full compliance tools",
            int.MaxValue,
            ["Unlimited transactions",
             "Everything in Pro",
             "GDPR compliance (DSAR, erasure, data export)",
             "Network analysis (fan-in/fan-out)",
             "Scheduled report delivery",
             "Unlimited webhook endpoints",
             "Unlimited API keys",
             "Unlimited team members"],
            _config["FlutterwaveBilling:EnterprisePlanId"] ?? "",
            false)
    ];

    // ── Private: Webhook Handlers ─────────────────────────────────────────────

    private async Task HandleChargeCompletedAsync(FlutterwaveWebhookEvent evt, CancellationToken ct)
    {
        var data = evt.Data;
        if (data == null) return;

        var customerEmail = data.Customer?.Email ?? string.Empty;
        var txRef = data.TxRef ?? string.Empty;
        var flwRef = data.FlwRef ?? string.Empty;
        var paymentPlan = data.PaymentPlan;

        // Only process successful charges
        if (!string.Equals(data.Status, "successful", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("charge.completed with non-successful status {Status} for {Email}", data.Status, customerEmail);
            return;
        }

        // Extract tenantId from tx_ref (format: payguard-{tenantId}-{plan}-{timestamp})
        var tenantId = ExtractTenantIdFromTxRef(txRef);

        // Find subscription by email or tenantId
        var sub = await _db.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.BillingEmail == customerEmail || s.TenantId == tenantId, ct);

        if (sub == null)
        {
            // Create new subscription
            sub = new TenantSubscription
            {
                TenantId = tenantId ?? customerEmail,
                BillingEmail = customerEmail,
                Provider = "flutterwave"
            };
            _db.TenantSubscriptions.Add(sub);
        }

        var plan = MapPlanIdToPlan(paymentPlan?.ToString());
        sub.ProviderCustomerId = data.Customer?.Id?.ToString() ?? string.Empty;
        sub.ProviderSubscriptionId = flwRef;
        sub.ProviderPlanCode = paymentPlan?.ToString() ?? string.Empty;
        sub.Provider = "flutterwave";
        sub.Plan = plan;
        sub.Status = "active";
        sub.IncludedTransactions = PlanLimits[plan];
        sub.PeriodStart = DateTime.UtcNow;
        sub.PeriodEnd = DateTime.UtcNow.AddMonths(1);
        sub.TransactionsThisPeriod = 0;
        sub.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Flutterwave subscription activated for {Email}: Plan={Plan}, FlwRef={FlwRef}",
            customerEmail, plan, flwRef);
    }

    private async Task HandleSubscriptionCancelledAsync(FlutterwaveWebhookEvent evt, CancellationToken ct)
    {
        var data = evt.Data;
        var customerEmail = data?.Customer?.Email ?? string.Empty;

        var sub = await _db.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.BillingEmail == customerEmail && s.Provider == "flutterwave", ct);

        if (sub == null)
        {
            _logger.LogWarning("subscription.cancelled for unknown Flutterwave customer {Email}", customerEmail);
            return;
        }

        sub.Status = "canceled";
        sub.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _alerting.AlertAsync($"🔴 Tenant {sub.TenantId} canceled their {sub.Plan} Flutterwave subscription.", ct);
        _logger.LogWarning("Flutterwave subscription canceled for tenant {TenantId}", sub.TenantId);
    }

    private async Task HandlePaymentFailedAsync(FlutterwaveWebhookEvent evt, CancellationToken ct)
    {
        var data = evt.Data;
        var customerEmail = data?.Customer?.Email ?? string.Empty;
        var amount = data?.Amount ?? 0;

        var sub = await _db.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.BillingEmail == customerEmail && s.Provider == "flutterwave", ct);

        var tenantId = sub?.TenantId ?? customerEmail;

        if (sub != null)
        {
            sub.Status = "past_due";
            sub.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        await _alerting.AlertAsync(
            $"❌ Flutterwave payment failed for tenant {tenantId} — ${amount:F2} due. " +
            $"Subscription may be suspended if not resolved.", ct);

        _logger.LogWarning("Flutterwave payment failed for tenant {TenantId}, amount {Amount}", tenantId, amount);
    }

    // ── Private: Helpers ──────────────────────────────────────────────────────

    private static string? ExtractTenantIdFromTxRef(string txRef)
    {
        // tx_ref format: payguard-{tenantId}-{plan}-{timestamp}
        if (string.IsNullOrWhiteSpace(txRef) || !txRef.StartsWith("payguard-"))
            return null;

        var parts = txRef.Split('-');
        // parts[0] = "payguard", parts[1] = tenantId (may contain hyphens), last 2 = plan + timestamp
        if (parts.Length < 4) return null;

        // Reconstruct tenantId from middle parts (tenant IDs can contain hyphens like "afriex-demo")
        var tenantParts = parts[1..^2]; // everything between "payguard-" and "-{plan}-{timestamp}"
        return string.Join("-", tenantParts);
    }

    private async Task<T?> PostAsync<T>(string path, object payload, CancellationToken ct) where T : class
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(path, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Flutterwave POST {Path} failed ({StatusCode}): {Body}", path, response.StatusCode, body);
            return null;
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private string GetPlanId(BillingPlan plan) => plan switch
    {
        BillingPlan.Starter    => _config["FlutterwaveBilling:StarterPlanId"] ?? string.Empty,
        BillingPlan.Pro        => _config["FlutterwaveBilling:ProPlanId"] ?? string.Empty,
        BillingPlan.Enterprise => _config["FlutterwaveBilling:EnterprisePlanId"] ?? string.Empty,
        _ => string.Empty
    };

    private static decimal GetPlanAmount(BillingPlan plan) => plan switch
    {
        BillingPlan.Starter    => 99m,
        BillingPlan.Pro        => 499m,
        BillingPlan.Enterprise => 2000m,
        _ => 0m
    };

    private BillingPlan MapPlanIdToPlan(string? planId)
    {
        if (planId == _config["FlutterwaveBilling:EnterprisePlanId"]) return BillingPlan.Enterprise;
        if (planId == _config["FlutterwaveBilling:ProPlanId"])        return BillingPlan.Pro;
        if (planId == _config["FlutterwaveBilling:StarterPlanId"])    return BillingPlan.Starter;
        return BillingPlan.Starter; // default
    }

    // ── JSON Options ──────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    // ── Flutterwave Response DTOs ─────────────────────────────────────────────

    private class FlutterwavePaymentResponse
    {
        public string Status { get; set; } = string.Empty;
        public string? Message { get; set; }
        public FlutterwavePaymentData? Data { get; set; }
    }

    private class FlutterwavePaymentData
    {
        public string Link { get; set; } = string.Empty;
    }

    private class FlutterwaveWebhookEvent
    {
        public string Event { get; set; } = string.Empty;
        public FlutterwaveEventData? Data { get; set; }
    }

    private class FlutterwaveEventData
    {
        public long Id { get; set; }
        [JsonPropertyName("tx_ref")]
        public string? TxRef { get; set; }
        [JsonPropertyName("flw_ref")]
        public string? FlwRef { get; set; }
        public decimal Amount { get; set; }
        public string? Currency { get; set; }
        public string? Status { get; set; }
        [JsonPropertyName("payment_plan")]
        public object? PaymentPlan { get; set; }
        public FlutterwaveCustomer? Customer { get; set; }
    }

    private class FlutterwaveCustomer
    {
        public long? Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? Name { get; set; }
        [JsonPropertyName("phone_number")]
        public string? Phone { get; set; }
    }
}
