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
        // Amount must be non-zero (kobo) — the plan overrides it, but amount=0 breaks the redirect.
        var amountKobo = GetPlanAmountKobo(plan);
        var payload = new
        {
            email,
            amount = amountKobo,
            plan = planCode,
            callback_url = callbackUrl,
            channels = new[] { "card", "bank", "bank_transfer" },
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

    // ── Verify Checkout (on redirect back from Paystack) ──────────────────────

    public async Task<TenantSubscription?> VerifyCheckoutAsync(
        string tenantId, string reference, CancellationToken ct = default)
    {
        // Call Paystack's verify endpoint using raw JSON to avoid DTO deserialization issues
        var httpResponse = await _http.GetAsync($"/transaction/verify/{reference}", ct);
        var body = await httpResponse.Content.ReadAsStringAsync(ct);

        if (!httpResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Paystack verify HTTP {StatusCode} for {Reference}: {Body}",
                httpResponse.StatusCode, reference, body);
            return null;
        }

        _logger.LogInformation("Paystack verify raw response for {Reference}: {Body}", reference, body);

        // Parse with JsonDocument for maximum resilience
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Check top-level status
        if (!root.TryGetProperty("status", out var statusProp) || !statusProp.GetBoolean())
        {
            _logger.LogWarning("Paystack verify status=false for {Reference}", reference);
            return null;
        }

        if (!root.TryGetProperty("data", out var data))
        {
            _logger.LogWarning("Paystack verify no data for {Reference}", reference);
            return null;
        }

        // Check transaction status
        var txStatus = data.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (!string.Equals(txStatus, "success", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Transaction {Reference} status={Status}", reference, txStatus);
            return null;
        }

        // Extract customer info
        var customerEmail = "";
        var customerCode = "";
        if (data.TryGetProperty("customer", out var customer))
        {
            customerEmail = customer.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
            customerCode = customer.TryGetProperty("customer_code", out var c) ? c.GetString() ?? "" : "";
        }

        // Extract plan code (may be in data.plan.plan_code)
        var planCode = "";
        if (data.TryGetProperty("plan", out var planObj) && planObj.ValueKind == JsonValueKind.Object)
        {
            planCode = planObj.TryGetProperty("plan_code", out var pc) ? pc.GetString() ?? "" : "";
        }

        // Extract plan from metadata (fallback)
        var metadataPlanStr = "";
        if (data.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
        {
            // Paystack may nest under "custom_fields" or return flat
            if (metadata.TryGetProperty("plan", out var mp))
                metadataPlanStr = mp.GetString() ?? "";
        }

        // Determine the billing plan
        BillingPlan plan;
        if (!string.IsNullOrEmpty(planCode))
        {
            plan = MapPlanCodeToPlan(planCode);
        }
        else if (Enum.TryParse<BillingPlan>(metadataPlanStr, ignoreCase: true, out var parsed))
        {
            plan = parsed;
        }
        else
        {
            _logger.LogWarning("Could not determine plan from verify response. planCode={PlanCode}, metadata={Meta}",
                planCode, metadataPlanStr);
            plan = BillingPlan.Starter; // safe fallback
        }

        // Find or create subscription
        var sub = await _db.TenantSubscriptions
            .FirstOrDefaultAsync(s2 => s2.TenantId == tenantId, ct);

        if (sub == null)
        {
            sub = new TenantSubscription
            {
                TenantId = tenantId,
                BillingEmail = customerEmail,
                Provider = "paystack"
            };
            _db.TenantSubscriptions.Add(sub);
        }

        sub.BillingEmail = !string.IsNullOrEmpty(customerEmail) ? customerEmail : sub.BillingEmail;
        sub.ProviderCustomerId = !string.IsNullOrEmpty(customerCode) ? customerCode : sub.ProviderCustomerId;
        sub.ProviderPlanCode = planCode;
        sub.Provider = "paystack";
        sub.Plan = plan;
        sub.Status = "active";
        sub.IncludedTransactions = PlanLimits[plan];
        sub.PeriodStart = DateTime.UtcNow;
        sub.PeriodEnd = DateTime.UtcNow.AddMonths(1);
        sub.TransactionsThisPeriod = 0;
        sub.UpdatedAt = DateTime.UtcNow;

        // Try to look up the Paystack subscription code so "Manage Subscription" works
        if (string.IsNullOrEmpty(sub.ProviderSubscriptionId) && !string.IsNullOrEmpty(customerCode))
        {
            var subCode = await LookupSubscriptionCodeAsync(customerCode, planCode, ct);
            if (!string.IsNullOrEmpty(subCode))
            {
                sub.ProviderSubscriptionId = subCode;
                _logger.LogInformation("Found subscription code {SubCode} for tenant {TenantId}", subCode, tenantId);
            }
            else
            {
                _logger.LogInformation(
                    "No subscription code found yet for tenant {TenantId} — webhook will set it later", tenantId);
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Checkout verified for tenant {TenantId}: Plan={Plan}, Reference={Reference}, Email={Email}, SubId={SubId}",
            tenantId, plan, reference, customerEmail, sub.ProviderSubscriptionId ?? "(pending)");

        return sub;
    }

    // ── Manage Subscription (card update link) ────────────────────────────────

    public async Task<string> GetManageSubscriptionUrlAsync(string tenantId, CancellationToken ct = default)
    {
        var sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"No subscription found for tenant {tenantId}");

        // If subscription code is missing, try to look it up from Paystack
        if (string.IsNullOrWhiteSpace(sub.ProviderSubscriptionId) && !string.IsNullOrWhiteSpace(sub.ProviderCustomerId))
        {
            _logger.LogInformation("ProviderSubscriptionId missing for tenant {TenantId}, looking up from Paystack...", tenantId);
            var subCode = await LookupSubscriptionCodeAsync(
                sub.ProviderCustomerId, sub.ProviderPlanCode ?? "", ct);
            if (!string.IsNullOrEmpty(subCode))
            {
                sub.ProviderSubscriptionId = subCode;
                sub.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Found and saved subscription code {SubCode} for tenant {TenantId}", subCode, tenantId);
            }
        }

        if (string.IsNullOrWhiteSpace(sub.ProviderSubscriptionId))
            throw new InvalidOperationException("Subscription is still being set up by Paystack. Please try again in a moment.");

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
             "Basic CSV export",
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
             "Compliance reports & CSV export",
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
             "Network analysis (fan-in/fan-out)",
             "Scheduled report delivery",
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
        var amount = GetAmountFromElement(data?.Amount) / 100m;

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

    /// <summary>
    /// Look up a customer's subscription code via Paystack's customer fetch API.
    /// GET /customer/{customer_code} returns subscriptions embedded in the customer object.
    /// The subscription list API requires a numeric ID, but the customer API accepts the code.
    /// </summary>
    private async Task<string?> LookupSubscriptionCodeAsync(
        string customerCode, string planCode, CancellationToken ct)
    {
        try
        {
            // Paystack GET /customer/{customer_code} — returns full customer with subscriptions array
            var path = $"/customer/{Uri.EscapeDataString(customerCode)}";
            var response = await _http.GetAsync(path, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("Paystack customer lookup for {Code}: HTTP {Status}, body length={Len}",
                customerCode, (int)response.StatusCode, body.Length);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Paystack customer lookup failed ({StatusCode}): {Body}",
                    response.StatusCode, body);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var data))
                return null;

            // Customer object has "subscriptions" array
            if (!data.TryGetProperty("subscriptions", out var subsArr) || subsArr.ValueKind != JsonValueKind.Array)
            {
                _logger.LogInformation("Customer {Code} has no subscriptions array", customerCode);
                return null;
            }

            _logger.LogInformation("Customer {Code} has {Count} subscription(s)", customerCode, subsArr.GetArrayLength());

            // Find an active subscription, prefer one matching the plan code
            string? bestMatch = null;
            foreach (var item in subsArr.EnumerateArray())
            {
                var status = item.TryGetProperty("status", out var st) ? st.GetString() : null;
                var subCode = item.TryGetProperty("subscription_code", out var sc) ? sc.GetString() : null;

                _logger.LogInformation("  Subscription {SubCode}: status={Status}", subCode ?? "(null)", status ?? "(null)");

                if (!string.Equals(status, "active", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(status, "non-renewing", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(status, "attention", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrEmpty(subCode)) continue;

                // If plan matches, return immediately
                if (!string.IsNullOrEmpty(planCode) && item.TryGetProperty("plan", out var planObj))
                {
                    var pc = planObj.ValueKind == JsonValueKind.Object
                        ? (planObj.TryGetProperty("plan_code", out var pcVal) ? pcVal.GetString() : null)
                        : null;
                    if (string.Equals(pc, planCode, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("  → Matched by plan code: {SubCode}", subCode);
                        return subCode;
                    }
                }

                bestMatch ??= subCode; // fallback to first active subscription
            }

            if (bestMatch != null)
                _logger.LogInformation("  → Best match (no plan match): {SubCode}", bestMatch);
            else
                _logger.LogInformation("  → No active subscriptions found");

            return bestMatch;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error looking up subscription code for customer {CustomerCode}", customerCode);
            return null;
        }
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

    /// <summary>
    /// Plan amounts in kobo (100 kobo = ₦1). These match the Paystack dashboard plan amounts.
    /// The plan code overrides the amount, but Paystack requires a non-zero amount for
    /// proper redirect behavior after checkout.
    /// </summary>
    private static long GetPlanAmountKobo(BillingPlan plan) => plan switch
    {
        BillingPlan.Starter    => 15_000_000,   // ₦150,000
        BillingPlan.Pro        => 80_000_000,   // ₦800,000
        BillingPlan.Enterprise => 320_000_000,  // ₦3,200,000
        _ => 15_000_000
    };

    private BillingPlan MapPlanCodeToPlan(string? planCode)
    {
        if (planCode == _config["Paystack:EnterprisePlanCode"]) return BillingPlan.Enterprise;
        if (planCode == _config["Paystack:ProPlanCode"])        return BillingPlan.Pro;
        if (planCode == _config["Paystack:StarterPlanCode"])    return BillingPlan.Starter;
        return BillingPlan.Starter; // default
    }

    /// <summary>
    /// Fall back to extracting the plan from transaction metadata when plan_code is unavailable.
    /// </summary>
    private static BillingPlan MapMetadataToPlan(PaystackMetadata? metadata)
    {
        var planStr = metadata?.Plan;
        if (string.IsNullOrEmpty(planStr)) return BillingPlan.Starter;
        return Enum.TryParse<BillingPlan>(planStr, ignoreCase: true, out var plan) ? plan : BillingPlan.Starter;
    }

    // ── JSON Options ──────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        // Paystack sends some numeric fields (e.g. plan.amount) as strings — allow both
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    // ── Paystack Response DTOs ────────────────────────────────────────────────
    // NOTE: Paystack is inconsistent — some fields arrive as numbers in one event
    // type and as strings in another. All numeric fields use JsonElement to handle both.

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

    private class PaystackVerifyResponse
    {
        public bool Status { get; set; }
        public string? Message { get; set; }
        public PaystackVerifyData? Data { get; set; }
    }

    private class PaystackVerifyData
    {
        public string Status { get; set; } = string.Empty;
        public string? Reference { get; set; }
        public JsonElement? Amount { get; set; }
        public PaystackCustomer? Customer { get; set; }
        public PaystackPlan? Plan { get; set; }
        public PaystackMetadata? Metadata { get; set; }
    }

    private class PaystackMetadata
    {
        [JsonPropertyName("tenantId")]
        public string? TenantId { get; set; }
        [JsonPropertyName("plan")]
        public string? Plan { get; set; }
    }

    private class PaystackWebhookEvent
    {
        public string Event { get; set; } = string.Empty;
        public PaystackEventData? Data { get; set; }
    }

    private class PaystackEventData
    {
        public JsonElement? Amount { get; set; }
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
        public JsonElement? Amount { get; set; }
    }

    /// <summary>Safely extract a numeric value from a JsonElement that may be a number or a string.</summary>
    private static long GetAmountFromElement(JsonElement? element)
    {
        if (element == null) return 0;
        var el = element.Value;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.GetInt64(),
            JsonValueKind.String => long.TryParse(el.GetString(), out var v) ? v : 0,
            _ => 0
        };
    }
}
