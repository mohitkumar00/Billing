using BillMate.Domain.Common;
using BillMate.Domain.Exceptions;

namespace BillMate.Domain.Entities;

public sealed class CreditNote : BaseEntity
{
    public Guid OriginalInvoiceId { get; private set; }
    public Guid? CustomerId { get; private set; }
    public string CreditNoteNumber { get; private set; } = string.Empty;
    public string FinancialYear { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public decimal TaxableAmount { get; private set; }
    public decimal CgstTotal { get; private set; }
    public decimal SgstTotal { get; private set; }
    public decimal IgstTotal { get; private set; }
    public decimal CessTotal { get; private set; }
    public decimal TotalAmount { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; } = DateTimeOffset.UtcNow;

    private CreditNote() { }

    internal static CreditNote CreateFromInvoice(Invoice invoice, string creditNoteNumber, string reason)
    {
        if (string.IsNullOrWhiteSpace(creditNoteNumber))
            throw new DomainException("VAL_054", "Credit note number is required.");

        return new CreditNote
        {
            BusinessId = invoice.BusinessId,
            OriginalInvoiceId = invoice.Id,
            CustomerId = invoice.CustomerId,
            CreditNoteNumber = creditNoteNumber.Trim(),
            FinancialYear = invoice.FinancialYear,
            Reason = reason.Trim(),
            TaxableAmount = invoice.TaxableAmount,
            CgstTotal = invoice.CgstTotal,
            SgstTotal = invoice.SgstTotal,
            IgstTotal = invoice.IgstTotal,
            CessTotal = invoice.CessTotal,
            TotalAmount = invoice.GrandTotal
        };
    }
}
