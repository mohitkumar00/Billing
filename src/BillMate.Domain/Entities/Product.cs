using BillMate.Domain.Common;
using BillMate.Domain.Exceptions;

namespace BillMate.Domain.Entities;

public sealed class Product : BaseEntity, IAggregateRoot
{
    public Guid? CategoryId { get; private set; }
    public Guid TaxSlabId { get; private set; }
    public string? HsnSacCode { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string? Barcode { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? Brand { get; private set; }
    public string Unit { get; private set; } = "PCS";
    public bool IsService { get; private set; }
    public bool IsTaxInclusive { get; private set; } = true;
    public bool IsRcmApplicable { get; private set; }
    public decimal PurchasePrice { get; private set; }
    public decimal SellingPrice { get; private set; }
    public decimal Mrp { get; private set; }
    public decimal CurrentStock { get; private set; }
    public decimal LowStockThreshold { get; private set; } = 5m;
    public bool TrackBatches { get; private set; }
    public string? ImagePath { get; private set; }
    public bool IsFavourite { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int RowVersion { get; private set; } = 1;

    public TaxSlab? TaxSlab { get; private set; }

    private Product() { }

    public static Product Create(
        Guid businessId,
        Guid taxSlabId,
        string sku,
        string name,
        decimal sellingPrice,
        decimal purchasePrice = 0m,
        decimal mrp = 0m,
        decimal openingStock = 0m,
        decimal lowStockThreshold = 5m,
        string unit = "PCS",
        string? barcode = null,
        string? hsnSacCode = null,
        bool isService = false,
        bool isTaxInclusive = true,
        bool isRcmApplicable = false,
        string? imagePath = null)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new DomainException("VAL_030", "Product SKU is required.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("VAL_031", "Product name is required.");
        if (sellingPrice < 0m || purchasePrice < 0m || mrp < 0m)
            throw new DomainException("VAL_032", "Product prices cannot be negative.");
        if (!isService && openingStock < 0m)
            throw new DomainException("VAL_033", "Opening stock cannot be negative for physical goods.");

        return new Product
        {
            BusinessId = businessId,
            TaxSlabId = taxSlabId,
            Sku = sku.Trim().ToUpperInvariant(),
            Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim(),
            HsnSacCode = string.IsNullOrWhiteSpace(hsnSacCode) ? null : hsnSacCode.Trim(),
            Name = name.Trim(),
            Unit = string.IsNullOrWhiteSpace(unit) ? "PCS" : unit.Trim().ToUpperInvariant(),
            IsService = isService,
            IsTaxInclusive = isTaxInclusive,
            IsRcmApplicable = isRcmApplicable,
            PurchasePrice = purchasePrice,
            SellingPrice = sellingPrice,
            Mrp = mrp > 0m ? mrp : sellingPrice,
            CurrentStock = isService ? 0m : openingStock,
            LowStockThreshold = lowStockThreshold,
            ImagePath = string.IsNullOrWhiteSpace(imagePath) ? null : imagePath.Trim()
        };
    }

    public void SetImage(string? imagePath)
    {
        ImagePath = string.IsNullOrWhiteSpace(imagePath) ? null : imagePath.Trim();
        MarkUpdated();
    }

    public bool IsLowStock() => !IsService && CurrentStock <= LowStockThreshold;

    public decimal DeductStock(decimal quantity)
    {
        if (quantity <= 0m)
            throw new DomainException("VAL_010", "Quantity must be greater than zero.");

        if (IsService)
            return 0m;

        if (CurrentStock < quantity)
            throw new InsufficientStockException(Id, Name, CurrentStock, quantity);

        CurrentStock = decimal.Round(CurrentStock - quantity, 3, MidpointRounding.AwayFromZero);
        RowVersion++;
        MarkUpdated();
        return CurrentStock;
    }

    public decimal AddStock(decimal quantity)
    {
        if (quantity <= 0m)
            throw new DomainException("VAL_010", "Quantity must be greater than zero.");

        if (IsService)
            return 0m;

        CurrentStock = decimal.Round(CurrentStock + quantity, 3, MidpointRounding.AwayFromZero);
        RowVersion++;
        MarkUpdated();
        return CurrentStock;
    }
}
