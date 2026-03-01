namespace PayGuardAI.Core.Entities;

/// <summary>
/// A billing invoice generated for a tenant's subscription period.
/// Invoices are auto-generated at subscription renewal or on demand.
/// </summary>
public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Human-readable invoice number: INV-2026-0001</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>Billing plan at time of invoice</summary>
    public BillingPlan Plan { get; set; } = BillingPlan.Starter;

    /// <summary>Invoice status: draft, issued, paid, overdue, void</summary>
    public string Status { get; set; } = "issued";

    /// <summary>Amount in cents (e.g. 29900 = $299.00)</summary>
    public int AmountCents { get; set; }

    /// <summary>Currency code (USD, NGN, etc.)</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Tax amount in cents (e.g. VAT)</summary>
    public int TaxCents { get; set; }

    /// <summary>Total = Amount + Tax, in cents</summary>
    public int TotalCents { get; set; }

    /// <summary>Billing period start</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>Billing period end</summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>Date the invoice was issued</summary>
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Date the invoice was paid (null if unpaid)</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>Due date for payment</summary>
    public DateTime DueDate { get; set; } = DateTime.UtcNow.AddDays(30);

    /// <summary>Payment provider reference (Paystack/Flutterwave transaction ID)</summary>
    public string? ProviderReference { get; set; }

    /// <summary>Payment provider name</summary>
    public string? Provider { get; set; }

    /// <summary>Transactions processed during this billing period</summary>
    public int TransactionsProcessed { get; set; }

    /// <summary>Included transactions for this plan</summary>
    public int IncludedTransactions { get; set; }

    // ── Bill-To Details (snapshot at time of invoice) ──

    /// <summary>Organization name</summary>
    public string BillToName { get; set; } = string.Empty;

    /// <summary>Billing email</summary>
    public string BillToEmail { get; set; } = string.Empty;

    /// <summary>Optional billing address line</summary>
    public string? BillToAddress { get; set; }

    /// <summary>Optional VAT / Tax ID</summary>
    public string? TaxId { get; set; }

    /// <summary>Internal notes (not shown on invoice)</summary>
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ── Computed Properties ──

    public decimal Amount => AmountCents / 100m;
    public decimal Tax => TaxCents / 100m;
    public decimal Total => TotalCents / 100m;

    /// <summary>
    /// Generate a unique invoice number based on date and sequence.
    /// </summary>
    public static string GenerateInvoiceNumber(int sequence)
        => $"INV-{DateTime.UtcNow:yyyy}-{sequence:D4}";
}
