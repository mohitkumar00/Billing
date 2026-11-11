using BillMate.Domain.Common;
using BillMate.Domain.Enums;
using BillMate.Domain.Exceptions;
using BillMate.Domain.Services;
using BillMate.Domain.ValueObjects;

namespace BillMate.Domain.Entities;

public sealed class Invoice : BaseEntity, IAggregateRoot
{
    private readonly List<InvoiceItem> _items = [];
    private readonly List<Payment> _payments = [];

    public Guid? CustomerId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? DeviceId { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public string InvoiceNumber { get; private set; } = string.Empty;
    public string FinancialYear { get; private set; } = string.Empty;
    public InvoiceType InvoiceType { get; private set; } = InvoiceType.TaxInvoice;
    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;
    public DateTimeOffset InvoiceDate { get; private set; } = DateTimeOffset.UtcNow;
    public DateOnly? DueDate { get; private set; }
    public string PlaceOfSupplyCode { get; private set; } = string.Empty;
    public bool IsInterState { get; private set; }
    public bool IsCompositionScheme { get; private set; }
    public bool IsReverseCharge { get; private set; }
    public string? EwayBillNumber { get; private set; }
    public string? VehicleNumber { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxableAmount { get; private set; }
    public decimal CgstTotal { get; private set; }
    public decimal SgstTotal { get; private set; }
    public decimal IgstTotal { get; private set; }
    public decimal CessTotal { get; private set; }
    public decimal RoundOff { get; private set; }
    public decimal GrandTotal { get; private set; }
    public decimal AmountPaid { get; private set; }
    public decimal AmountDue => decimal.Round(GrandTotal - AmountPaid, 2, MidpointRounding.AwayFromZero);

    public string? Notes { get; private set; }
    public string? TermsConditions { get; private set; }
    public string? PdfPath { get; private set; }
    public int RowVersion { get; private set; } = 1;

    public IReadOnlyCollection<InvoiceItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<Payment> Payments => _payments.AsReadOnly();

    private Invoice() { }

    public static Invoice CreateDraft(
        Guid businessId,
        Guid createdByUserId,
        Guid idempotencyKey,
        string invoiceNumber,
        string sellerStateCode,
        string placeOfSupplyCode,
        bool isCompositionScheme,
        InvoiceType requestedType = InvoiceType.TaxInvoice,
        Guid? customerId = null,
        Guid? deviceId = null,
        DateTimeOffset? invoiceDate = null,
        DateOnly? dueDate = null,
        bool isReverseCharge = false,
        string? ewayBillNumber = null,
        string? vehicleNumber = null,
        string? notes = null)
    {
        if (idempotencyKey == Guid.Empty)
            throw new DomainException("VAL_050", "Idempotency key is required.");
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            throw new DomainException("VAL_051", "Invoice number is required.");
        if (string.IsNullOrWhiteSpace(placeOfSupplyCode) || placeOfSupplyCode.Trim().Length != 2)
            throw new DomainException("VAL_052", "Valid 2-digit place of supply code is required.");

        var effectiveDate = invoiceDate ?? DateTimeOffset.UtcNow;
        var fy = ValueObjects.FinancialYear.FromDate(effectiveDate);
        var effectiveType = isCompositionScheme && requestedType == InvoiceType.TaxInvoice
            ? InvoiceType.BillOfSupply
            : requestedType;

        var normalizedSellerState = sellerStateCode.Trim();
        var normalizedPos = placeOfSupplyCode.Trim();

        return new Invoice
        {
            BusinessId = businessId,
            CustomerId = customerId,
            CreatedByUserId = createdByUserId,
            DeviceId = deviceId,
            IdempotencyKey = idempotencyKey,
            InvoiceNumber = invoiceNumber.Trim(),
            FinancialYear = fy.Code,
            InvoiceType = effectiveType,
            Status = InvoiceStatus.Draft,
            InvoiceDate = effectiveDate,
            DueDate = dueDate,
            PlaceOfSupplyCode = normalizedPos,
            IsInterState = !string.Equals(normalizedSellerState, normalizedPos, StringComparison.Ordinal),
            IsCompositionScheme = isCompositionScheme,
            IsReverseCharge = isReverseCharge,
            EwayBillNumber = string.IsNullOrWhiteSpace(ewayBillNumber) ? null : ewayBillNumber.Trim(),
            VehicleNumber = string.IsNullOrWhiteSpace(vehicleNumber) ? null : vehicleNumber.Trim().ToUpperInvariant(),
            Notes = notes?.Trim()
        };
    }

    public InvoiceItem AddItem(
        Guid productId,
        string itemName,
        string unit,
        decimal quantity,
        decimal unitPrice,
        decimal discountAmount,
        decimal gstRatePercent,
        decimal cessRatePercent,
        bool isTaxInclusive,
        string? hsnSacCode = null,
        Guid? variantId = null,
        Guid? batchId = null)
    {
        EnsureMutable();

        var breakdown = GstCalculator.CalculateLine(
            quantity: quantity,
            unitPrice: unitPrice,
            discountAmount: discountAmount,
            gstRatePercent: gstRatePercent,
            cessRatePercent: cessRatePercent,
            isTaxInclusive: isTaxInclusive,
            isInterState: IsInterState,
            isCompositionScheme: IsCompositionScheme);

        var item = InvoiceItem.Create(
            businessId: BusinessId,
            invoiceId: Id,
            productId: productId,
            itemName: itemName,
            hsnSacCode: hsnSacCode,
            unit: unit,
            quantity: quantity,
            unitPrice: unitPrice,
            discountAmount: discountAmount,
            breakdown: breakdown,
            variantId: variantId,
            batchId: batchId);

        _items.Add(item);
        RecalculateTotals();
        return item;
    }

    public void ParkOnHold()
    {
        if (Status != InvoiceStatus.Draft)
            throw new DomainException("BILL_002", "Only draft invoices can be placed on hold.");
        if (_items.Count == 0)
            throw new DomainException("BILL_004", "Cannot hold an invoice with no items.");

        Status = InvoiceStatus.Hold;
        MarkUpdated();
    }

    public void Confirm()
    {
        if (Status is not (InvoiceStatus.Draft or InvoiceStatus.Hold))
            throw new DomainException("BILL_002", $"Invoice in status '{Status}' cannot be confirmed.");
        if (_items.Count == 0)
            throw new DomainException("BILL_004", "Cannot confirm an invoice with no line items.");

        RecalculateTotals();
        Status = AmountPaid >= GrandTotal && GrandTotal > 0m
            ? InvoiceStatus.Paid
            : AmountPaid > 0m
                ? InvoiceStatus.Partial
                : InvoiceStatus.Confirmed;
        RowVersion++;
        MarkUpdated();
    }

    public Payment RecordPayment(Guid receivedByUserId, PaymentMode mode, decimal amount, string? referenceNumber = null, string? notes = null)
    {
        if (Status == InvoiceStatus.Cancelled)
            throw new DomainException("BILL_005", "Cannot record payment against a cancelled invoice.");
        if (Status is InvoiceStatus.Draft or InvoiceStatus.Hold)
            throw new DomainException("BILL_006", "Invoice must be confirmed before recording payment.");
        if (amount <= 0m)
            throw new DomainException("VAL_043", "Payment amount must be greater than zero.");
        if (amount > AmountDue)
            throw new DomainException("BILL_007", $"Payment ₹{amount:N2} exceeds remaining amount due ₹{AmountDue:N2}.");

        var payment = Payment.Create(
            businessId: BusinessId,
            invoiceId: Id,
            customerId: CustomerId,
            receivedByUserId: receivedByUserId,
            mode: mode,
            amount: amount,
            referenceNumber: referenceNumber,
            notes: notes);

        _payments.Add(payment);
        AmountPaid = decimal.Round(AmountPaid + amount, 2, MidpointRounding.AwayFromZero);
        Status = AmountDue <= 0m ? InvoiceStatus.Paid : InvoiceStatus.Partial;
        RowVersion++;
        MarkUpdated();
        return payment;
    }

    public CreditNote Cancel(string reason, string creditNoteNumber)
    {
        if (Status == InvoiceStatus.Cancelled)
            throw new DomainException("BILL_008", "Invoice is already cancelled.");
        if (Status is InvoiceStatus.Draft or InvoiceStatus.Hold)
            throw new DomainException("BILL_009", "Draft or held invoices can be discarded directly without a credit note.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("VAL_053", "Cancellation reason is required.");

        Status = InvoiceStatus.Cancelled;
        RowVersion++;
        MarkUpdated();

        return CreditNote.CreateFromInvoice(this, creditNoteNumber, reason);
    }

    private void EnsureMutable()
    {
        if (Status is not (InvoiceStatus.Draft or InvoiceStatus.Hold))
            throw new DomainException("BILL_002", "Confirmed or cancelled invoices cannot be edited; issue a Credit Note instead.");
    }

    private void RecalculateTotals()
    {
        Subtotal = decimal.Round(_items.Sum(i => i.Quantity * i.UnitPrice), 2, MidpointRounding.AwayFromZero);
        DiscountTotal = decimal.Round(_items.Sum(i => i.DiscountAmount), 2, MidpointRounding.AwayFromZero);
        TaxableAmount = decimal.Round(_items.Sum(i => i.TaxableAmount), 2, MidpointRounding.AwayFromZero);
        CgstTotal = decimal.Round(_items.Sum(i => i.CgstAmount), 2, MidpointRounding.AwayFromZero);
        SgstTotal = decimal.Round(_items.Sum(i => i.SgstAmount), 2, MidpointRounding.AwayFromZero);
        IgstTotal = decimal.Round(_items.Sum(i => i.IgstAmount), 2, MidpointRounding.AwayFromZero);
        CessTotal = decimal.Round(_items.Sum(i => i.CessAmount), 2, MidpointRounding.AwayFromZero);

        var rawTotal = new Money(_items.Sum(i => i.LineTotal));
        var (roundedTotal, roundOffDelta) = rawTotal.RoundToNearestRupee();
        RoundOff = roundOffDelta;
        GrandTotal = roundedTotal.Amount;
        MarkUpdated();
    }
}
