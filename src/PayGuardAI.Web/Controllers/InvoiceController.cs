using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayGuardAI.Data.Services;
using PayGuardAI.Web.Services;

namespace PayGuardAI.Web.Controllers;

/// <summary>
/// API controller for invoice PDF downloads.
/// </summary>
[ApiController]
[Route("api/invoices")]
[Authorize(Policy = "RequireManager")]
public class InvoiceController : ControllerBase
{
    private readonly InvoiceService _invoiceService;
    private readonly InvoicePdfService _pdfService;

    public InvoiceController(InvoiceService invoiceService, InvoicePdfService pdfService)
    {
        _invoiceService = invoiceService;
        _pdfService = pdfService;
    }

    /// <summary>
    /// Download a PDF invoice by ID.
    /// </summary>
    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> DownloadPdf(Guid id, CancellationToken ct)
    {
        var invoice = await _invoiceService.GetInvoiceAsync(id, ct);

        if (invoice == null)
            return NotFound(new { error = "Invoice not found" });

        var pdfBytes = _pdfService.GeneratePdf(invoice);

        return File(pdfBytes, "application/pdf", $"{invoice.InvoiceNumber}.pdf");
    }
}
