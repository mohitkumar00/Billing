using BillMate.Domain.Common;
using BillMate.Domain.ValueObjects;

namespace BillMate.Domain.Entities;

public sealed class InvoiceItem : BaseEntity
{
    public Guid InvoiceId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? VariantId { get; private set; }
    public Guid? BatchId { get; private set; }
    public string ItemName { get; private set; } = string.Empty;
    public string? HsnSacCode { get; private set; }
    public string Unit { get; private set; } = "PCS";
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal TaxableAmount { get; private set; }
    public decimal CgstRate { get; private set; }
    public decimal CgstAmount { get; private set; }
    public decimal SgstRate { get; private set; }
    public decimal SgstAmount { get; private set; }
    public decimal IgstRate { get; private set; }
    public decimal IgstAmount { get; private set; }
    public decimal CessRate { get; private set; }
    public decimal CessAmount { get; private set; }
    public decimal LineTotal { get; private set; }

    private InvoiceItem() { }

    internal static InvoiceItem Create(
        Guid businessId,
        Guid invoiceId,
        Guid productId,
        string itemName,
        string? hsnSacCode,
        string unit,
        decimal quantity,
        decimal unitPrice,
        decimal discountAmount,
        TaxBreakdown breakdown,
        Guid? variantId,
        Guid? batchId)
    {
        return new InvoiceItem
        {
            BusinessId = businessId,
            InvoiceId = invoiceId,
            ProductId = productId,
            VariantId = variantId,
            BatchId = batchId,
            ItemName = itemName.Trim(),
            HsnSacCode = string.IsNullOrWhiteSpace(hsnSacCode) ? null : hsnSacCode.Trim(),
            Unit = unit.Trim().ToUpperInvariant(),
            Quantity = quantity,
            UnitPrice = unitPrice,
            DiscountAmount = discountAmount,
            TaxableAmount = breakdown.TaxableAmount,
            CgstRate = breakdown.CgstRate,
            CgstAmount = breakdown.CgstAmount,
            SgstRate = breakdown.SgstRate,
            SgstAmount = breakdown.SgstAmount,
            IgstRate = breakdown.IgstRate,
            IgstAmount = breakdown.IgstAmount,
            CessRate = breakdown.CessRate,
            CessAmount = breakdown.CessAmount,
            LineTotal = breakdown.LineTotal
        };
    }
}
