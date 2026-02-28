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
/// Paystack billing implementation.
/// Feature flag: FeatureFlags:BillingEnabled
///
/// Paystack config keys (set in Railway env vars for production):
///   Paystack:SecretKey        — sk_live_xxx  (sk_test_xxx for testing)
///   Paystack:PublicKey        — pk_live_xxx  (pk_test_xxx for testing)
///   Paystack:WebhookSecret    — (same as SecretKey for HMAC verification)
///   Paystack:StarterPlanCode  — PLN_xxx
///   Paystack:ProPlanCode      — PLN_xxx
///   Paystack:EnterprisePlanCode — PLN_xxx
///
/// Paystack API docs: https://paystack.com/docs/api/
/// Plans: https://paystack.com/docs/api/plan/
/// Subscriptions: https://paystack.com/docs/api/subscription/
/// Webhooks: https://paystack.com/docs/payments/webhooks/
/// </summary>
public class PaystackBillingService : IBillingService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly IAlertingService _alerting;
    private readonly ILogger<PaystackBillingService> _logger;
    private readonly HttpClient _http;

    private const string PaystackBaseUrl = "https://api.paystack.co";

    // Plan limits
    private static readonly Dictionary<BillingPlan, int> PlanLimits = new()
    {
        [BillingPlan.Trial]      = 100,
        [BillingPlan.Starter]    = 1_000,
        [BillingPlan.Pro]        = 10_000,
        [BillingPlan.Enterprise] = int.MaxValue
    };

    public PaystackBillingService(
        ApplicationDbContext db,
        IConfiguration config,
        IAlertingService alerting,
        ILogger<PaystackBillingService> logger,
        HttpClient http)
    {
        _db = db;
        _config = config;
        _alerting = alerting;
        _logger = logger;
        _http = http;

        _http.BaseAddress = new Uri(PaystackBaseUrl);
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config["Paystack:SecretKey"] ?? ""}");
    }

    // ── Checkout (Initialize Transaction with Plan) ───────────────────────────

    public async Task<string> CreateCheckoutSessionAsync(
        string tenantId, string email, BillingPlan plan,
        string callbackUrl, CancellationToken ct = default)
    {
        var planCode = GetPlanCode(plan);
        if (string.IsNullOrWhiteSpace(planCode))
            throw new InvalidOperationException($"No Paystack plan code configured for plan {plan}. Set Paystack:{plan}PlanCode in config.");

        // Paystack uses "initialize transaction" with a plan code to start a subscription.
        // Amount is required by the API even with a plan (plan overrides it).
        var payload = new
        {
            email,
            amount = 0,
            plan = planCode,
            callback_url = callbackUrl,
            metadata = new { tenantId, plan = plan.ToString() }
        };

        var response = await PostAsync<PaystackInitResponse>("/transaction/initialize", payload, ct);

        if (response?.Status != true || response.Data == null)
            throw new InvalidOperationException("Paystack transaction initialization failed: " + (response?.Message ?? "unknown error"));

        _logger.LogInformation("Created Paystack checkout for tenant {TenantId}, plan {Plan}, ref {Reference}",
            tenantId, plan, response.Data.Reference);

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
                Provider = "paystack",
                ProviderPlanCode = planCode
            };
            _db.TenantSubscriptions.Add(existing);
            await _db.SaveChangesAsync(ct);
        }

        return response.Data.AuthorizationUrl;
    }

    // ── Manage Subscription (card update link) ────────────────────────────────

    public async Task<string> GetManageSubscriptionUrlAsync(string tenantId, CancellationToken ct = default)
    {
        var sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"No subscription found for tenant {tenantId}");

        if (string.IsNullOrWhiteSpace(sub.ProviderSubscriptionId))
            throw new InvalidOperationException("Tenant has no active subscription — they need to complete checkout first.");

        // Paystack generates a link to manage/update the card on a subscription
        var response = await GetAsync<PaystackManageLinkResponse>(
            $"/subscription/{sub.ProviderSubscriptionId}/manage/link", ct);

        if (response?.Status != true || response.Data == null)
            throw new InvalidOperationException("Could not generate subscription management link.");

        _logger.LogInformation("Generated manage link for tenant {TenantId}", tenantId);
        return response.Data.Link;
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
            await _alerting.AlertAsync(
                $"⚠️ Tenant {tenantId} is {sub.TransactionsThisPeriod - limit} transactions over their {sub.Plan} plan limit ({limit}/mo). " +
                $"Consider upgrading or overage billing will apply.", ct);
        }

        return !isOverage;
    }

    // ── Webhook Handler ───────────────────────────────────────────────────────

    public async Task HandleWebhookAsync(string payload, string signature, CancellationToken ct = default)
    {
        // Verify HMAC SHA512 signature using secret key
        var secretKey = _config["Paystack:SecretKey"] ?? string.Empty;
        var computedHash = ComputeHmacSha512(payload, secretKey);

        if (!string.Equals(computedHash, signature, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Paystack webhook signature validation failed");
            throw new InvalidOperationException("Invalid Paystack webhook signature");
        }

        var webhookEvent = JsonSerializer.Deserialize<PaystackWebhookEvent>(payload, JsonOptions);
        if (webhookEvent == null)
        {
            _logger.LogWarning("Could not deserialize Paystack webhook payload");
            return;
        }

        _logger.LogInformation("Processing Paystack webhook event: {EventType}", webhookEvent.Event);

        switch (webhookEvent.Event)
        {
            case "subscription.create":
                await HandleSubscriptionCreatedAsync(webhookEvent, ct);
                break;

            case "charge.success":
                await HandleChargeSuccessAsync(webhookEvent, ct);
                break;

            case "subscription.disable":
            case "subscription.not_renew":
                await HandleSubscriptionDisabledAsync(webhookEvent, ct);
                break;

            case "invoice.payment_failed":
                await HandlePaymentFailedAsync(webhookEvent, ct);
                break;

            case "invoice.create":
            case "invoice.update":
                _logger.LogInformation("Invoice event: {Event}", webhookEvent.Event);
                break;

            default:
                _logger.LogDebug("Unhandled Paystack event: {EventType}", webhookEvent.Event);
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
             "Compliance reports & CSV export",
             "Email alerts",
             "Watchlists (manual entry)",
             "Customer risk profiles",
             "Full audit trail",
             "2 API keys",
             "5 team members"],
            _config["Paystack:StarterPlanCode"] ?? "",
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
             "Scheduled report delivery",
             "Network analysis (fan-in/fan-out)",
             "Outbound webhooks (5 endpoints)",
             "Slack real-time alerts",
             "Watchlist CSV import",
             "Custom roles & permissions",
             "10 API keys",
             "25 team members"],
            _config["Paystack:ProPlanCode"] ?? "",
            true),

        new(BillingPlan.Enterprise,
            "Enterprise",
            "$2,000/mo",
            "Unlimited protection with full compliance tools",
            int.MaxValue,
            ["Unlimited transactions",
             "Everything in Pro",
             "GDPR compliance (DSAR, erasure, data export)",
             "Unlimited webhook endpoints",
             "Unlimited API keys",
             "Unlimited team members"],
            _config["Paystack:EnterprisePlanCode"] ?? "",
            false)
    ];

    // ── Private: Webhook Handlers ─────────────────────────────────────────────

    private async Task HandleSubscriptionCreatedAsync(PaystackWebhookEvent evt, CancellationToken ct)
    {
        var data = evt.Data;
        var customerEmail = data?.Customer?.Email ?? string.Empty;
        var subscriptionCode = data?.SubscriptionCode ?? string.Empty;
        var planCode = data?.Plan?.PlanCode ?? string.Empty;
        var emailToken = data?.EmailToken ?? string.Empty;

        // Find tenant by email or by plan code from pending records
        var sub = await _db.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.BillingEmail == customerEmail || s.ProviderPlanCode == planCode, ct);

        if (sub == null)
        {
            // Create new subscription record
            sub = new TenantSubscription
            {
                TenantId = customerEmail, // fallback — will be linked properly when OAuth is on
                BillingEmail = customerEmail,
                Provider = "paystack"
            };
            _db.TenantSubscriptions.Add(sub);
        }

        var plan = MapPlanCodeToPlan(planCode);
        sub.ProviderCustomerId = data?.Customer?.CustomerCode ?? string.Empty;
        sub.ProviderSubscriptionId = subscriptionCode;
        sub.ProviderPlanCode = planCode;
        sub.ProviderEmailToken = emailToken;
        sub.Provider = "paystack";
        sub.Plan = plan;
        sub.Status = "active";
        sub.IncludedTransactions = PlanLimits[plan];
        sub.PeriodStart = DateTime.UtcNow;
        sub.PeriodEnd = DateTime.UtcNow.AddMonths(1);
        sub.TransactionsThisPeriod = 0;
        sub.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Subscription created for {Email}: Plan={Plan}, SubCode={SubCode}",
            customerEmail, plan, subscriptionCode);
    }

    private async Task HandleChargeSuccessAsync(PaystackWebhookEvent evt, CancellationToken ct)
    {
        var data = evt.Data;
        var customerEmail = data?.Customer?.Email ?? string.Empty;

        var sub = await _db.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.BillingEmail == customerEmail, ct);

        if (sub == null)
        {
            _logger.LogDebug("charge.success for unknown customer {Email} — may be a non-subscription charge", customerEmail);
            return;
        }

        // Renewal: reset period counters
        sub.Status = "active";
        sub.TransactionsThisPeriod = 0;
        sub.PeriodStart = DateTime.UtcNow;
        sub.PeriodEnd = DateTime.UtcNow.AddMonths(1);
        sub.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Subscription renewed for {Email}, plan {Plan}", customerEmail, sub.Plan);
    }

    private async Task HandleSubscriptionDisabledAsync(PaystackWebhookEvent evt, CancellationToken ct)
    {
        var data = evt.Data;
        var subscriptionCode = data?.SubscriptionCode ?? string.Empty;

        var sub = await _db.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.ProviderSubscriptionId == subscriptionCode, ct);

        if (sub == null)
        {
            _logger.LogWarning("subscription.disable for unknown subscription {SubCode}", subscriptionCode);
            return;
        }

        sub.Status = "canceled";
        sub.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _alerting.AlertAsync($"🔴 Tenant {sub.TenantId} canceled their {sub.Plan} subscription.", ct);
        _logger.LogWarning("Subscription canceled for tenant {TenantId}", sub.TenantId);
    }

    private async Task HandlePaymentFailedAsync(PaystackWebhookEvent evt, CancellationToken ct)
    {
        var data = evt.Data;
        var customerEmail = data?.Customer?.Email ?? string.Empty;
        var amount = (data?.Amount ?? 0) / 100m;

        var sub = await _db.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.BillingEmail == customerEmail, ct);

        var tenantId = sub?.TenantId ?? customerEmail;

        if (sub != null)
        {
            sub.Status = "past_due";
            sub.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        await _alerting.AlertAsync(
            $"❌ Payment failed for tenant {tenantId} — ${amount:F2} due. " +
            $"Subscription may be suspended if not resolved.", ct);

        _logger.LogWarning("Payment failed for tenant {TenantId}, amount {Amount}", tenantId, amount);
    }

    // ── Private: HTTP Helpers ─────────────────────────────────────────────────

    private async Task<T?> PostAsync<T>(string path, object payload, CancellationToken ct) where T : class
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(path, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Paystack POST {Path} failed ({StatusCode}): {Body}", path, response.StatusCode, body);
            return null;
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct) where T : class
    {
        var response = await _http.GetAsync(path, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Paystack GET {Path} failed ({StatusCode}): {Body}", path, response.StatusCode, body);
            return null;
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private static string ComputeHmacSha512(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA512(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        return Convert.ToHexStringLower(hash);
    }

    private string GetPlanCode(BillingPlan plan) => plan switch
    {
        BillingPlan.Starter    => _config["Paystack:StarterPlanCode"] ?? string.Empty,
        BillingPlan.Pro        => _config["Paystack:ProPlanCode"] ?? string.Empty,
        BillingPlan.Enterprise => _config["Paystack:EnterprisePlanCode"] ?? string.Empty,
        _ => string.Empty
    };

    private BillingPlan MapPlanCodeToPlan(string? planCode)
    {
        if (planCode == _config["Paystack:EnterprisePlanCode"]) return BillingPlan.Enterprise;
        if (planCode == _config["Paystack:ProPlanCode"])        return BillingPlan.Pro;
        if (planCode == _config["Paystack:StarterPlanCode"])    return BillingPlan.Starter;
        return BillingPlan.Starter; // default
    }

    // ── JSON Options ──────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    // ── Paystack Response DTOs ────────────────────────────────────────────────

    private class PaystackInitResponse
    {
        public bool Status { get; set; }
        public string? Message { get; set; }
        public PaystackInitData? Data { get; set; }
    }

    private class PaystackInitData
    {
        [JsonPropertyName("authorization_url")]
        public string AuthorizationUrl { get; set; } = string.Empty;
        [JsonPropertyName("access_code")]
        public string AccessCode { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
    }

    private class PaystackManageLinkResponse
    {
        public bool Status { get; set; }
        public string? Message { get; set; }
        public PaystackManageLinkData? Data { get; set; }
    }

    private class PaystackManageLinkData
    {
        public string Link { get; set; } = string.Empty;
    }

    private class PaystackWebhookEvent
    {
        public string Event { get; set; } = string.Empty;
        public PaystackEventData? Data { get; set; }
    }

    private class PaystackEventData
    {
        public long Amount { get; set; }
        public string? Reference { get; set; }
        public string? Status { get; set; }
        [JsonPropertyName("subscription_code")]
        public string? SubscriptionCode { get; set; }
        [JsonPropertyName("email_token")]
        public string? EmailToken { get; set; }
        public PaystackCustomer? Customer { get; set; }
        public PaystackPlan? Plan { get; set; }
    }

    private class PaystackCustomer
    {
        public string Email { get; set; } = string.Empty;
        [JsonPropertyName("customer_code")]
        public string CustomerCode { get; set; } = string.Empty;
    }

    private class PaystackPlan
    {
        [JsonPropertyName("plan_code")]
        public string PlanCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Amount { get; set; }
    }
}
