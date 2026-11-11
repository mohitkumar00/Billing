using BillMate.Domain.Common;
using BillMate.Domain.Enums;
using BillMate.Domain.Exceptions;

namespace BillMate.Domain.Entities;

public sealed class Supplier : BaseEntity, IAggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string? ContactPerson { get; private set; }
    public string Phone { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Gstin { get; private set; }
    public string? Address { get; private set; }
    public string? State { get; private set; }
    public string? StateCode { get; private set; }
    public int PaymentTermsDays { get; private set; } = 30;
    public decimal OutstandingPayable { get; private set; }

    private Supplier() { }

    public static Supplier Create(
        Guid businessId,
        string name,
        string phone,
        string? contactPerson = null,
        string? gstin = null,
        string? state = null,
        string? stateCode = null,
        int paymentTermsDays = 30)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("VAL_090", "Supplier name is required.");

        var validatedGstin = ValueObjects.Gstin.CreateOptional(gstin);
        return new Supplier
        {
            BusinessId = businessId,
            Name = name.Trim(),
            ContactPerson = contactPerson?.Trim(),
            Phone = phone.Trim(),
            Gstin = validatedGstin?.Value,
            State = state?.Trim(),
            StateCode = validatedGstin?.StateCode ?? stateCode?.Trim(),
            PaymentTermsDays = paymentTermsDays
        };
    }
}

public sealed class PurchaseOrder : BaseEntity, IAggregateRoot
{
    private readonly List<PurchaseOrderItem> _items = [];

    public Guid SupplierId { get; private set; }
    public string PoNumber { get; private set; } = string.Empty;
    public string? SupplierInvoiceNo { get; private set; }
    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Ordered;
    public DateOnly OrderDate { get; private set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly? ReceivedDate { get; private set; }
    public decimal TaxableAmount { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal GrandTotal { get; private set; }
    public string? Notes { get; private set; }

    public IReadOnlyCollection<PurchaseOrderItem> Items => _items.AsReadOnly();

    private PurchaseOrder() { }

    public static PurchaseOrder Create(Guid businessId, Guid supplierId, string poNumber, string? notes = null) => new()
    {
        BusinessId = businessId,
        SupplierId = supplierId,
        PoNumber = poNumber.Trim().ToUpperInvariant(),
        Notes = notes?.Trim()
    };

    public PurchaseOrderItem AddItem(Guid productId, decimal orderedQty, decimal unitCost, decimal taxAmount, Guid? variantId = null)
    {
        var item = PurchaseOrderItem.Create(BusinessId, Id, productId, orderedQty, unitCost, taxAmount, variantId);
        _items.Add(item);
        Recalculate();
        return item;
    }

    public void MarkReceived(string? supplierInvoiceNo = null)
    {
        if (Status == PurchaseOrderStatus.Received)
            throw new DomainException("PO_001", "Purchase order is already received.");
        if (Status == PurchaseOrderStatus.Cancelled)
            throw new DomainException("PO_002", "Cancelled purchase order cannot be received.");

        Status = PurchaseOrderStatus.Received;
        ReceivedDate = DateOnly.FromDateTime(DateTime.UtcNow);
        SupplierInvoiceNo = supplierInvoiceNo?.Trim();
        MarkUpdated();
    }

    private void Recalculate()
    {
        TaxableAmount = decimal.Round(_items.Sum(i => i.OrderedQty * i.UnitCost), 2, MidpointRounding.AwayFromZero);
        TaxTotal = decimal.Round(_items.Sum(i => i.TaxAmount), 2, MidpointRounding.AwayFromZero);
        GrandTotal = decimal.Round(TaxableAmount + TaxTotal, 2, MidpointRounding.AwayFromZero);
    }
}

public sealed class PurchaseOrderItem : BaseEntity
{
    public Guid PurchaseOrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? VariantId { get; private set; }
    public decimal OrderedQty { get; private set; }
    public decimal ReceivedQty { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal LineTotal { get; private set; }

    private PurchaseOrderItem() { }

    internal static PurchaseOrderItem Create(
        Guid businessId,
        Guid purchaseOrderId,
        Guid productId,
        decimal orderedQty,
        decimal unitCost,
        decimal taxAmount,
        Guid? variantId)
    {
        if (orderedQty <= 0m)
            throw new DomainException("VAL_091", "Ordered quantity must be greater than zero.");

        var lineTotal = decimal.Round(orderedQty * unitCost + taxAmount, 2, MidpointRounding.AwayFromZero);
        return new PurchaseOrderItem
        {
            BusinessId = businessId,
            PurchaseOrderId = purchaseOrderId,
            ProductId = productId,
            VariantId = variantId,
            OrderedQty = orderedQty,
            UnitCost = unitCost,
            TaxAmount = taxAmount,
            LineTotal = lineTotal
        };
    }

    public void SetReceivedQuantity(decimal receivedQty)
    {
        if (receivedQty < 0m)
            throw new DomainException("VAL_092", "Received quantity cannot be negative.");
        ReceivedQty = receivedQty;
        MarkUpdated();
    }
}

public sealed class ExpenseCategory : BaseEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private ExpenseCategory() { }

    public static ExpenseCategory Create(Guid businessId, string name, string? description = null) => new()
    {
        BusinessId = businessId,
        Name = name.Trim(),
        Description = description?.Trim()
    };
}

public sealed class Expense : BaseEntity
{
    public Guid ExpenseCategoryId { get; private set; }
    public Guid? SupplierId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMode PaymentMode { get; private set; }
    public DateOnly ExpenseDate { get; private set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public string Description { get; private set; } = string.Empty;
    public string? ReceiptImagePath { get; private set; }

    private Expense() { }

    public static Expense Create(
        Guid businessId,
        Guid expenseCategoryId,
        Guid createdByUserId,
        decimal amount,
        PaymentMode paymentMode,
        string description,
        DateOnly? expenseDate = null,
        Guid? supplierId = null)
    {
        if (amount <= 0m)
            throw new DomainException("VAL_093", "Expense amount must be greater than zero.");

        return new Expense
        {
            BusinessId = businessId,
            ExpenseCategoryId = expenseCategoryId,
            CreatedByUserId = createdByUserId,
            SupplierId = supplierId,
            Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
            PaymentMode = paymentMode,
            ExpenseDate = expenseDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            Description = description.Trim()
        };
    }
}

public sealed class LoyaltyTransaction : BaseEntity
{
    public Guid CustomerId { get; private set; }
    public Guid? InvoiceId { get; private set; }
    public int PointsDelta { get; private set; }
    public int BalanceAfter { get; private set; }
    public string TransactionType { get; private set; } = "EARN";
    public string? Notes { get; private set; }

    private LoyaltyTransaction() { }

    public static LoyaltyTransaction Create(
        Guid businessId,
        Guid customerId,
        int pointsDelta,
        int balanceAfter,
        string transactionType,
        Guid? invoiceId = null,
        string? notes = null) => new()
    {
        BusinessId = businessId,
        CustomerId = customerId,
        InvoiceId = invoiceId,
        PointsDelta = pointsDelta,
        BalanceAfter = balanceAfter,
        TransactionType = transactionType.Trim().ToUpperInvariant(),
        Notes = notes?.Trim()
    };
}
