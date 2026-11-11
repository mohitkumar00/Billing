using BillMate.Application.Common.Exceptions;
using BillMate.Application.Common.Interfaces;
using BillMate.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BillMate.Application.Features.Invoices;

public sealed record InvoicePdfResult(byte[] Content, string FileName, string ContentType);

public sealed record GetInvoicePdfQuery(Guid InvoiceId, InvoicePageFormat Format = InvoicePageFormat.A4) : IQuery<InvoicePdfResult>;

public sealed record GetThermalReceiptBytesQuery(Guid InvoiceId, int PaperWidthMm = 80, bool OpenCashDrawer = false) : IQuery<byte[]>;

internal sealed class InvoicePrintHandlers :
    IRequestHandler<GetInvoicePdfQuery, InvoicePdfResult>,
    IRequestHandler<GetThermalReceiptBytesQuery, byte[]>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IInvoicePdfGenerator _pdfGenerator;
    private readonly IEscPosReceiptBuilder _escPosBuilder;

    public InvoicePrintHandlers(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IInvoicePdfGenerator pdfGenerator,
        IEscPosReceiptBuilder escPosBuilder)
    {
        _db = db;
        _currentUser = currentUser;
        _pdfGenerator = pdfGenerator;
        _escPosBuilder = escPosBuilder;
    }

    public async Task<InvoicePdfResult> Handle(GetInvoicePdfQuery request, CancellationToken ct)
    {
        var model = await BuildPdfModelAsync(request.InvoiceId, ct);
        var pdfBytes = _pdfGenerator.GenerateInvoicePdf(model, request.Format);
        var safeFileName = $"{model.InvoiceNumber.Replace('/', '-')}.pdf";
        return new InvoicePdfResult(pdfBytes, safeFileName, "application/pdf");
    }

    public async Task<byte[]> Handle(GetThermalReceiptBytesQuery request, CancellationToken ct)
    {
        var model = await BuildPdfModelAsync(request.InvoiceId, ct);
        return _escPosBuilder.BuildReceipt(model, request.PaperWidthMm, request.OpenCashDrawer);
    }

    private async Task<InvoicePdfModel> BuildPdfModelAsync(Guid invoiceId, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var invoice = await _db.Invoices.AsNoTracking()
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.BusinessId == businessId, ct)
            ?? throw new NotFoundException("BILL_001", "Invoice not found.");

        var business = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == businessId, ct)
            ?? throw new NotFoundException("BUS_001", "Business not found.");

        var customer = invoice.CustomerId.HasValue
            ? await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == invoice.CustomerId.Value, ct)
            : null;

        var upiVpa = await _db.AppSettings.AsNoTracking()
            .Where(s => s.BusinessId == businessId && s.SettingKey == "upi_vpa")
            .Select(s => s.SettingValue)
            .FirstOrDefaultAsync(ct);

        var addrParts = new[] { business.AddressLine1, business.AddressLine2, business.City, $"{business.State} - {business.Pincode}" }
            .Where(p => !string.IsNullOrWhiteSpace(p));

        return new InvoicePdfModel(
            BusinessName: business.Name,
            BusinessGstin: business.Gstin,
            BusinessAddress: string.Join(", ", addrParts),
            BusinessPhone: business.Phone,
            BusinessStateCode: business.StateCode,
            LogoPath: business.LogoPath,
            SignaturePath: business.SignaturePath,
            InvoiceNumber: invoice.InvoiceNumber,
            InvoiceType: invoice.InvoiceType,
            InvoiceDate: invoice.InvoiceDate,
            IsInterState: invoice.IsInterState,
            IsCompositionScheme: invoice.IsCompositionScheme,
            IsReverseCharge: invoice.IsReverseCharge,
            PlaceOfSupplyCode: invoice.PlaceOfSupplyCode,
            CustomerName: customer?.Name,
            CustomerGstin: customer?.Gstin,
            CustomerPhone: customer?.Phone,
            CustomerAddress: customer?.BillingAddress,
            EwayBillNumber: invoice.EwayBillNumber,
            VehicleNumber: invoice.VehicleNumber,
            Subtotal: invoice.Subtotal,
            DiscountTotal: invoice.DiscountTotal,
            TaxableAmount: invoice.TaxableAmount,
            CgstTotal: invoice.CgstTotal,
            SgstTotal: invoice.SgstTotal,
            IgstTotal: invoice.IgstTotal,
            CessTotal: invoice.CessTotal,
            RoundOff: invoice.RoundOff,
            GrandTotal: invoice.GrandTotal,
            AmountPaid: invoice.AmountPaid,
            AmountDue: invoice.AmountDue,
            AmountInWords: new Money(invoice.GrandTotal).ToIndianWords(),
            UpiVpa: upiVpa,
            Notes: invoice.Notes,
            Items: invoice.Items.Select(it => new InvoicePdfLineModel(
                it.ItemName,
                it.HsnSacCode,
                it.Unit,
                it.Quantity,
                it.UnitPrice,
                it.DiscountAmount,
                it.TaxableAmount,
                it.CgstRate,
                it.CgstAmount,
                it.SgstRate,
                it.SgstAmount,
                it.IgstRate,
                it.IgstAmount,
                it.CessAmount,
                it.LineTotal)).ToArray());
    }
}
