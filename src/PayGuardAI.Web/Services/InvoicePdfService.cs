using PayGuardAI.Core.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Generates professional PDF invoices using QuestPDF.
/// </summary>
public class InvoicePdfService
{
    static InvoicePdfService()
    {
        // QuestPDF Community license (free for revenue < $1M)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>
    /// Generate a PDF invoice document as a byte array.
    /// </summary>
    public byte[] GeneratePdf(Invoice invoice)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(c => ComposeHeader(c, invoice));
                page.Content().Element(c => ComposeContent(c, invoice));
                page.Footer().Element(ComposeFooter);
            });
        });

        return document.GeneratePdf();
    }

    private void ComposeHeader(IContainer container, Invoice invoice)
    {
        container.Column(col =>
        {
            col.Spacing(8);

            // Top bar: Company + Invoice label
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("PayGuard AI")
                        .Bold().FontSize(22).FontColor(Colors.Blue.Darken3);
                    left.Item().Text("Intelligent Fraud Detection Platform")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                });

                row.ConstantItem(160).AlignRight().Column(right =>
                {
                    right.Item().Text("INVOICE")
                        .Bold().FontSize(24).FontColor(Colors.Grey.Darken2);
                    right.Item().AlignRight().Text(invoice.InvoiceNumber)
                        .FontSize(12).FontColor(Colors.Blue.Darken3);
                });
            });

            // Divider
            col.Item().PaddingVertical(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

            // Invoice meta + Bill To
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("From:").Bold().FontSize(9).FontColor(Colors.Grey.Darken1);
                    left.Item().Text("PayGuard AI Inc.");
                    left.Item().Text("Fraud Detection & Compliance");
                    left.Item().Text("support@payguardai.xyz")
                        .FontColor(Colors.Blue.Darken2);
                });

                row.ConstantItem(20);

                row.RelativeItem().Column(mid =>
                {
                    mid.Item().Text("Bill To:").Bold().FontSize(9).FontColor(Colors.Grey.Darken1);
                    mid.Item().Text(invoice.BillToName);
                    mid.Item().Text(invoice.BillToEmail)
                        .FontColor(Colors.Blue.Darken2);
                    if (!string.IsNullOrWhiteSpace(invoice.BillToAddress))
                        mid.Item().Text(invoice.BillToAddress);
                    if (!string.IsNullOrWhiteSpace(invoice.TaxId))
                        mid.Item().Text($"Tax ID: {invoice.TaxId}")
                            .FontColor(Colors.Grey.Darken1);
                });

                row.ConstantItem(20);

                row.ConstantItem(140).AlignRight().Column(right =>
                {
                    MetaRow(right, "Invoice Date:", invoice.IssuedAt.ToString("MMM dd, yyyy"));
                    MetaRow(right, "Due Date:", invoice.DueDate.ToString("MMM dd, yyyy"));
                    MetaRow(right, "Status:", invoice.Status.ToUpperInvariant());
                    if (invoice.PaidAt.HasValue)
                        MetaRow(right, "Paid:", invoice.PaidAt.Value.ToString("MMM dd, yyyy"));
                });
            });

            col.Item().PaddingVertical(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
        });
    }

    private void ComposeContent(IContainer container, Invoice invoice)
    {
        container.PaddingVertical(10).Column(col =>
        {
            col.Spacing(15);

            // Line items table
            col.Item().Table(table =>
            {
                // Columns
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(4); // Description
                    columns.RelativeColumn(2); // Period
                    columns.RelativeColumn(1); // Qty
                    columns.RelativeColumn(1.5f); // Amount
                });

                // Header row
                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Description");
                    header.Cell().Element(HeaderCell).Text("Period");
                    header.Cell().Element(HeaderCell).AlignCenter().Text("Usage");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Amount");

                    static IContainer HeaderCell(IContainer c)
                        => c.DefaultTextStyle(x => x.Bold().FontSize(10).FontColor(Colors.White))
                            .Background(Colors.Blue.Darken3)
                            .Padding(6);
                });

                // Plan line item
                table.Cell().Element(DataCell).Text($"{invoice.Plan} Plan — Monthly Subscription");
                table.Cell().Element(DataCell).Text($"{invoice.PeriodStart:MMM dd} — {invoice.PeriodEnd:MMM dd, yyyy}");
                table.Cell().Element(DataCell).AlignCenter()
                    .Text($"{invoice.TransactionsProcessed:N0} txns");
                table.Cell().Element(DataCell).AlignRight()
                    .Text($"${invoice.Amount:N2}");

                // Included transactions line
                table.Cell().Element(DataCellAlt)
                    .Text($"    Included: {invoice.IncludedTransactions:N0} transactions/month")
                    .FontColor(Colors.Grey.Darken1);
                table.Cell().Element(DataCellAlt);
                table.Cell().Element(DataCellAlt);
                table.Cell().Element(DataCellAlt);

                static IContainer DataCell(IContainer c)
                    => c.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6);

                static IContainer DataCellAlt(IContainer c)
                    => c.Background(Colors.Grey.Lighten4).Padding(6);
            });

            // Totals
            col.Item().AlignRight().Width(220).Column(totals =>
            {
                totals.Spacing(4);

                TotalRow(totals, "Subtotal:", $"${invoice.Amount:N2}");

                if (invoice.TaxCents > 0)
                    TotalRow(totals, "Tax:", $"${invoice.Tax:N2}");
                else
                    TotalRow(totals, "Tax:", "$0.00");

                totals.Item().PaddingVertical(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                totals.Item().Row(row =>
                {
                    row.RelativeItem().Text("Total:").Bold().FontSize(13);
                    row.ConstantItem(100).AlignRight()
                        .Text($"${invoice.Total:N2}")
                        .Bold().FontSize(13).FontColor(Colors.Blue.Darken3);
                });

                if (invoice.Status == "paid")
                {
                    totals.Item().PaddingTop(6).AlignRight()
                        .Text("✓ PAID")
                        .Bold().FontSize(14).FontColor(Colors.Green.Darken2);
                }
            });

            // Payment info
            if (!string.IsNullOrWhiteSpace(invoice.Provider) || !string.IsNullOrWhiteSpace(invoice.ProviderReference))
            {
                col.Item().PaddingTop(10).Column(payment =>
                {
                    payment.Item().Text("Payment Information")
                        .Bold().FontSize(11).FontColor(Colors.Grey.Darken2);
                    payment.Item().PaddingTop(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                    payment.Spacing(3);

                    if (!string.IsNullOrWhiteSpace(invoice.Provider))
                        payment.Item().Text($"Payment Provider: {invoice.Provider}")
                            .FontColor(Colors.Grey.Darken1);
                    if (!string.IsNullOrWhiteSpace(invoice.ProviderReference))
                        payment.Item().Text($"Reference: {invoice.ProviderReference}")
                            .FontColor(Colors.Grey.Darken1);
                });
            }

            // Notes
            if (!string.IsNullOrWhiteSpace(invoice.Notes))
            {
                col.Item().PaddingTop(10).Background(Colors.Yellow.Lighten4).Padding(10).Column(notes =>
                {
                    notes.Item().Text("Notes").Bold().FontSize(10);
                    notes.Item().Text(invoice.Notes).FontSize(9);
                });
            }
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("PayGuard AI — ")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                    text.Span("Intelligent Fraud Detection for African Fintech")
                        .FontSize(8).FontColor(Colors.Grey.Medium);
                });
                row.ConstantItem(100).AlignRight().Text(text =>
                {
                    text.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                    text.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        });
    }

    private static void MetaRow(ColumnDescriptor col, string label, string value)
    {
        col.Item().Row(row =>
        {
            row.ConstantItem(85).Text(label)
                .FontSize(9).FontColor(Colors.Grey.Darken1);
            row.RelativeItem().AlignRight().Text(value)
                .FontSize(9).Bold();
        });
    }

    private static void TotalRow(ColumnDescriptor col, string label, string value)
    {
        col.Item().Row(row =>
        {
            row.RelativeItem().Text(label).FontSize(10);
            row.ConstantItem(100).AlignRight().Text(value).FontSize(10);
        });
    }
}
