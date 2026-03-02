using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Manages invoice lifecycle: creation, lookup, and auto-generation from subscriptions.
/// </summary>
public class InvoiceService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<InvoiceService> _logger;

    private static readonly Dictionary<BillingPlan, int> PlanPriceCents = new()
    {
        [BillingPlan.Trial] = 0,
        [BillingPlan.Starter] = 29900,     // $299
        [BillingPlan.Pro] = 89900,          // $899
        [BillingPlan.Enterprise] = 199900   // $1,999
    };

    private static readonly Dictionary<BillingPlan, int> PlanTransactions = new()
    {
        [BillingPlan.Trial] = 1000,
        [BillingPlan.Starter] = 1000,
        [BillingPlan.Pro] = 10000,
        [BillingPlan.Enterprise] = int.MaxValue
    };

    public InvoiceService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        ITenantContext tenantContext,
        ILogger<InvoiceService> logger)
    {
        _dbFactory = dbFactory;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Get all invoices for the current tenant, newest first.
    /// </summary>
    public async Task<List<Invoice>> GetInvoicesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenantId(_tenantContext.TenantId);

        return await db.Invoices
            .Where(i => i.TenantId == _tenantContext.TenantId)
            .OrderByDescending(i => i.IssuedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get a single invoice by ID (must belong to current tenant).
    /// </summary>
    public async Task<Invoice?> GetInvoiceAsync(Guid invoiceId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenantId(_tenantContext.TenantId);

        return await db.Invoices
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.TenantId == _tenantContext.TenantId, ct);
    }

    /// <summary>
    /// Generate an invoice from the current subscription.
    /// Used when subscription renews or for on-demand generation.
    /// </summary>
    public async Task<Invoice> GenerateFromSubscriptionAsync(TenantSubscription subscription, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenantId(_tenantContext.TenantId);

        // Guard: if an invoice already exists for this exact billing period, return it
        var existing = await db.Invoices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.TenantId == subscription.TenantId
                                   && i.PeriodStart == subscription.PeriodStart
                                   && i.PeriodEnd == subscription.PeriodEnd
                                   && i.Status != "void", ct);

        if (existing != null)
        {
            _logger.LogInformation(
                "Invoice {InvoiceNumber} already exists for tenant {TenantId} period {Start:d}–{End:d}, skipping",
                existing.InvoiceNumber, subscription.TenantId, subscription.PeriodStart, subscription.PeriodEnd);
            return existing;
        }

        // Get next sequence number for this tenant
        var existingCount = await db.Invoices
            .IgnoreQueryFilters()
            .CountAsync(i => i.TenantId == subscription.TenantId, ct);

        // Get org settings for bill-to details
        var orgSettings = await db.OrganizationSettings
            .FirstOrDefaultAsync(o => o.TenantId == subscription.TenantId, ct);

        var priceCents = PlanPriceCents.GetValueOrDefault(subscription.Plan, 0);

        var invoice = new Invoice
        {
            TenantId = subscription.TenantId,
            InvoiceNumber = Invoice.GenerateInvoiceNumber(existingCount + 1, subscription.TenantId),
            Plan = subscription.Plan,
            Status = subscription.Status == "active" ? "paid" : "issued",
            AmountCents = priceCents,
            Currency = "USD",
            TaxCents = 0, // No VAT for now
            TotalCents = priceCents,
            PeriodStart = subscription.PeriodStart,
            PeriodEnd = subscription.PeriodEnd,
            IssuedAt = DateTime.UtcNow,
            PaidAt = subscription.Status == "active" ? DateTime.UtcNow : null,
            DueDate = DateTime.UtcNow.AddDays(30),
            Provider = subscription.Provider,
            ProviderReference = subscription.ProviderSubscriptionId,
            TransactionsProcessed = subscription.TransactionsThisPeriod,
            IncludedTransactions = PlanTransactions.GetValueOrDefault(subscription.Plan, 1000),
            BillToName = orgSettings?.OrganizationName ?? "Unknown Organization",
            BillToEmail = subscription.BillingEmail,
        };

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Generated invoice {InvoiceNumber} for tenant {TenantId}, plan {Plan}, amount ${Amount}",
            invoice.InvoiceNumber, invoice.TenantId, invoice.Plan, invoice.Amount);

        return invoice;
    }

    /// <summary>
    /// Mark an invoice as paid.
    /// </summary>
    public async Task MarkPaidAsync(Guid invoiceId, string? providerReference = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var invoice = await db.Invoices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

        if (invoice == null) return;

        invoice.Status = "paid";
        invoice.PaidAt = DateTime.UtcNow;
        invoice.ProviderReference = providerReference ?? invoice.ProviderReference;
        invoice.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Void an invoice (e.g. if subscription was canceled).
    /// </summary>
    public async Task VoidInvoiceAsync(Guid invoiceId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var invoice = await db.Invoices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

        if (invoice == null) return;

        invoice.Status = "void";
        invoice.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
