using BillMate.Application.Common.Exceptions;
using BillMate.Application.Common.Interfaces;
using BillMate.Application.Common.Security;
using BillMate.Domain.Entities;
using BillMate.Domain.Enums;
using BillMate.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BillMate.Application.Features.Invoices;

public sealed record CreateInvoiceLineInput(
    Guid ProductId,
    decimal Quantity,
    decimal? CustomUnitPrice = null,
    decimal DiscountAmount = 0m);

public sealed record InvoicePaymentInput(
    PaymentMode PaymentMode,
    decimal Amount,
    string? ReferenceNumber = null);

[RequirePermission("invoices.create")]
public sealed record CreateInvoiceCommand(
    Guid IdempotencyKey,
    IReadOnlyList<CreateInvoiceLineInput> Items,
    IReadOnlyList<InvoicePaymentInput>? Payments = null,
    Guid? CustomerId = null,
    string? PlaceOfSupplyCode = null,
    InvoiceType InvoiceType = InvoiceType.TaxInvoice,
    bool IsHold = false,
    bool IsReverseCharge = false,
    string? EwayBillNumber = null,
    string? VehicleNumber = null,
    string? Notes = null) : ICommand<InvoiceDto>;

[RequirePermission("invoices.cancel")]
public sealed record CancelInvoiceCommand(Guid InvoiceId, string Reason) : ICommand<CreditNoteDto>;

public sealed record GetInvoiceByIdQuery(Guid InvoiceId) : IQuery<InvoiceDto>;

public sealed record GetInvoicesQuery(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    InvoiceStatus? Status = null,
    int Page = 1,
    int PageSize = 50) : IQuery<IReadOnlyList<InvoiceSummaryDto>>;

public sealed record InvoiceItemDto(
    Guid ProductId,
    string ItemName,
    string? HsnSacCode,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TaxableAmount,
    decimal CgstRate,
    decimal CgstAmount,
    decimal SgstRate,
    decimal SgstAmount,
    decimal IgstRate,
    decimal IgstAmount,
    decimal CessAmount,
    decimal LineTotal);

public sealed record InvoiceDto(
    Guid Id,
    string InvoiceNumber,
    string FinancialYear,
    InvoiceType InvoiceType,
    InvoiceStatus Status,
    DateTimeOffset InvoiceDate,
    Guid? CustomerId,
    string PlaceOfSupplyCode,
    bool IsInterState,
    bool IsCompositionScheme,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxableAmount,
    decimal CgstTotal,
    decimal SgstTotal,
    decimal IgstTotal,
    decimal CessTotal,
    decimal RoundOff,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal AmountDue,
    string AmountInWords,
    IReadOnlyList<InvoiceItemDto> Items);

public sealed record InvoiceSummaryDto(
    Guid Id,
    string InvoiceNumber,
    InvoiceType InvoiceType,
    InvoiceStatus Status,
    DateTimeOffset InvoiceDate,
    Guid? CustomerId,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal AmountDue);

public sealed record CreditNoteDto(
    Guid Id,
    Guid OriginalInvoiceId,
    string CreditNoteNumber,
    string Reason,
    decimal TotalAmount,
    DateTimeOffset IssuedAt);

public sealed class CreateInvoiceValidator : AbstractValidator<CreateInvoiceCommand>
{
    public CreateInvoiceValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Items).NotEmpty().WithMessage("At least one line item is required.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.DiscountAmount).GreaterThanOrEqualTo(0);
        });
    }
}

internal sealed class InvoiceHandlers :
    IRequestHandler<CreateInvoiceCommand, InvoiceDto>,
    IRequestHandler<CancelInvoiceCommand, CreditNoteDto>,
    IRequestHandler<GetInvoiceByIdQuery, InvoiceDto>,
    IRequestHandler<GetInvoicesQuery, IReadOnlyList<InvoiceSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISyncBroadcaster _broadcaster;

    public InvoiceHandlers(IApplicationDbContext db, ICurrentUserService currentUser, ISyncBroadcaster broadcaster)
    {
        _db = db;
        _currentUser = currentUser;
        _broadcaster = broadcaster;
    }

    public async Task<InvoiceDto> Handle(CreateInvoiceCommand request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var userId = _currentUser.UserId ?? throw new UnauthorizedAppException("AUTH_003", "Missing user context.");

        await _db.AcquireBusinessInvoiceLockAsync(businessId, ct);

        // 1. Idempotency Guard: return existing invoice if already processed
        var existing = await _db.Invoices
            .AsNoTracking()
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.BusinessId == businessId && i.IdempotencyKey == request.IdempotencyKey, ct);

        if (existing is not null)
            return Map(existing);

        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId, ct)
            ?? throw new NotFoundException("BUS_001", "Business profile not found.");

        Customer? customer = null;
        if (request.CustomerId.HasValue)
        {
            customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId.Value && c.BusinessId == businessId, ct)
                ?? throw new NotFoundException("CRM_004", "Customer not found.");
        }

        var now = DateTimeOffset.UtcNow;
        var fy = FinancialYear.FromDate(now);
        var countThisFy = await _db.Invoices.IgnoreQueryFilters()
            .CountAsync(i => i.BusinessId == businessId && i.FinancialYear == fy.Code, ct);

        var invoiceNumber = fy.FormatSequenceNumber(business.InvoicePrefix, countThisFy + 1);
        var placeOfSupply = !string.IsNullOrWhiteSpace(request.PlaceOfSupplyCode)
            ? request.PlaceOfSupplyCode.Trim()
            : customer?.StateCode ?? business.StateCode;

        var invoice = Invoice.CreateDraft(
            businessId: businessId,
            createdByUserId: userId,
            idempotencyKey: request.IdempotencyKey,
            invoiceNumber: invoiceNumber,
            sellerStateCode: business.StateCode,
            placeOfSupplyCode: placeOfSupply,
            isCompositionScheme: business.IsComposition,
            requestedType: request.InvoiceType,
            customerId: customer?.Id,
            deviceId: _currentUser.DeviceId,
            invoiceDate: now,
            isReverseCharge: request.IsReverseCharge,
            ewayBillNumber: request.EwayBillNumber,
            vehicleNumber: request.VehicleNumber,
            notes: request.Notes);

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToArray();
        var products = await _db.Products
            .Include(p => p.TaxSlab)
            .Where(p => p.BusinessId == businessId && productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var stockUpdates = new List<(Guid ProductId, decimal NewStock)>();

        foreach (var line in request.Items)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
                throw new NotFoundException("INV_001", $"Product '{line.ProductId}' not found.");

            var unitPrice = line.CustomUnitPrice ?? product.SellingPrice;
            var slab = product.TaxSlab ?? throw new NotFoundException("TAX_001", "Product tax slab missing.");

            invoice.AddItem(
                productId: product.Id,
                itemName: product.Name,
                unit: product.Unit,
                quantity: line.Quantity,
                unitPrice: unitPrice,
                discountAmount: line.DiscountAmount,
                gstRatePercent: slab.TotalRate,
                cessRatePercent: slab.CessRate,
                isTaxInclusive: product.IsTaxInclusive,
                hsnSacCode: product.HsnSacCode);

            if (!request.IsHold && !product.IsService && request.InvoiceType is InvoiceType.TaxInvoice or InvoiceType.BillOfSupply)
            {
                var balanceAfter = product.DeductStock(line.Quantity);
                _db.StockMovements.Add(StockMovement.Create(
                    businessId,
                    product.Id,
                    StockMovementType.Sale,
                    -line.Quantity,
                    balanceAfter,
                    userId,
                    "INVOICE",
                    invoice.Id,
                    $"Sale {invoice.InvoiceNumber}"));
                stockUpdates.Add((product.Id, balanceAfter));
            }
        }

        if (request.IsHold)
        {
            invoice.ParkOnHold();
        }
        else
        {
            invoice.Confirm();

            if (request.Payments is not null)
            {
                foreach (var pay in request.Payments)
                {
                    invoice.RecordPayment(userId, pay.PaymentMode, pay.Amount, pay.ReferenceNumber);
                }
            }

            if (customer is not null && invoice.AmountDue > 0m)
            {
                customer.AddCreditSale(invoice.AmountDue);
            }

            if (customer is not null && invoice.GrandTotal >= 100m)
            {
                var earned = (int)Math.Floor(invoice.GrandTotal / 100m);
                if (earned > 0)
                {
                    customer.EarnLoyaltyPoints(earned);
                    _db.LoyaltyTransactions.Add(LoyaltyTransaction.Create(
                        businessId, customer.Id, earned, customer.LoyaltyPoints, "EARN", invoice.Id, $"Earned on {invoice.InvoiceNumber}"));
                }
            }
        }

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync(ct);

        if (!request.IsHold)
        {
            await _broadcaster.BroadcastInvoiceCreatedAsync(businessId, invoice.Id, invoice.InvoiceNumber, invoice.GrandTotal, _currentUser.DeviceId, ct);
            foreach (var (productId, newStock) in stockUpdates)
            {
                await _broadcaster.BroadcastStockUpdatedAsync(businessId, productId, null, newStock, ct);
            }
        }

        return Map(invoice);
    }

    public async Task<CreditNoteDto> Handle(CancelInvoiceCommand request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var userId = _currentUser.UserId ?? throw new UnauthorizedAppException("AUTH_003", "Missing user context.");

        var invoice = await _db.Invoices
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId && i.BusinessId == businessId, ct)
            ?? throw new NotFoundException("BILL_001", "Invoice not found.");

        var fy = FinancialYear.FromDate(DateTimeOffset.UtcNow);
        var cnCount = await _db.CreditNotes.IgnoreQueryFilters()
            .CountAsync(c => c.BusinessId == businessId && c.FinancialYear == fy.Code, ct);
        var cnNumber = fy.FormatSequenceNumber("CN", cnCount + 1);

        var creditNote = invoice.Cancel(request.Reason, cnNumber);
        _db.CreditNotes.Add(creditNote);

        // Reverse stock movements for goods
        var productIds = invoice.Items.Select(i => i.ProductId).Distinct().ToArray();
        var products = await _db.Products
            .Where(p => p.BusinessId == businessId && productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        foreach (var item in invoice.Items)
        {
            if (products.TryGetValue(item.ProductId, out var product) && !product.IsService)
            {
                var balanceAfter = product.AddStock(item.Quantity);
                _db.StockMovements.Add(StockMovement.Create(
                    businessId,
                    product.Id,
                    StockMovementType.SaleReturn,
                    item.Quantity,
                    balanceAfter,
                    userId,
                    "CREDIT_NOTE",
                    creditNote.Id,
                    $"Cancelled {invoice.InvoiceNumber}: {request.Reason}"));
            }
        }

        if (invoice.CustomerId.HasValue && invoice.AmountDue > 0m)
        {
            var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == invoice.CustomerId.Value && c.BusinessId == businessId, ct);
            customer?.RecordPayment(invoice.AmountDue);
        }

        await _db.SaveChangesAsync(ct);
        return new CreditNoteDto(creditNote.Id, creditNote.OriginalInvoiceId, creditNote.CreditNoteNumber, creditNote.Reason, creditNote.TotalAmount, creditNote.IssuedAt);
    }

    public async Task<InvoiceDto> Handle(GetInvoiceByIdQuery request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var invoice = await _db.Invoices
            .AsNoTracking()
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId && i.BusinessId == businessId, ct)
            ?? throw new NotFoundException("BILL_001", "Invoice not found.");

        return Map(invoice);
    }

    public async Task<IReadOnlyList<InvoiceSummaryDto>> Handle(GetInvoicesQuery request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var query = _db.Invoices.AsNoTracking().Where(i => i.BusinessId == businessId);

        if (request.FromUtc.HasValue) query = query.Where(i => i.InvoiceDate >= request.FromUtc.Value);
        if (request.ToUtc.HasValue) query = query.Where(i => i.InvoiceDate <= request.ToUtc.Value);
        if (request.Status.HasValue) query = query.Where(i => i.Status == request.Status.Value);

        var skip = (Math.Max(1, request.Page) - 1) * Math.Clamp(request.PageSize, 1, 200);
        return await query
            .OrderByDescending(i => i.InvoiceDate)
            .Skip(skip)
            .Take(Math.Clamp(request.PageSize, 1, 200))
            .Select(i => new InvoiceSummaryDto(
                i.Id,
                i.InvoiceNumber,
                i.InvoiceType,
                i.Status,
                i.InvoiceDate,
                i.CustomerId,
                i.GrandTotal,
                i.AmountPaid,
                i.GrandTotal - i.AmountPaid))
            .ToListAsync(ct);
    }

    private static InvoiceDto Map(Invoice i) => new(
        i.Id,
        i.InvoiceNumber,
        i.FinancialYear,
        i.InvoiceType,
        i.Status,
        i.InvoiceDate,
        i.CustomerId,
        i.PlaceOfSupplyCode,
        i.IsInterState,
        i.IsCompositionScheme,
        i.Subtotal,
        i.DiscountTotal,
        i.TaxableAmount,
        i.CgstTotal,
        i.SgstTotal,
        i.IgstTotal,
        i.CessTotal,
        i.RoundOff,
        i.GrandTotal,
        i.AmountPaid,
        i.AmountDue,
        new Money(i.GrandTotal).ToIndianWords(),
        i.Items.Select(it => new InvoiceItemDto(
            it.ProductId,
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
