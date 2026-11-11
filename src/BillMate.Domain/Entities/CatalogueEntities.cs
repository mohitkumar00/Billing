using BillMate.Domain.Common;
using BillMate.Domain.Exceptions;

namespace BillMate.Domain.Entities;

public sealed class HsnSacCode : BaseEntity
{
    public string Code { get; private set; } = string.Empty;
    public string CodeType { get; private set; } = "HSN";
    public string Description { get; private set; } = string.Empty;
    public Guid? DefaultTaxSlabId { get; private set; }

    private HsnSacCode() { }

    public static HsnSacCode Create(Guid businessId, string code, string codeType, string description, Guid? defaultTaxSlabId = null)
    {
        var normalizedType = codeType.Trim().ToUpperInvariant();
        if (normalizedType is not ("HSN" or "SAC"))
            throw new DomainException("VAL_080", "Code type must be 'HSN' or 'SAC'.");

        return new HsnSacCode
        {
            BusinessId = businessId,
            Code = code.Trim(),
            CodeType = normalizedType,
            Description = description.Trim(),
            DefaultTaxSlabId = defaultTaxSlabId
        };
    }
}

public sealed class Category : BaseEntity
{
    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }

    private Category() { }

    public static Category Create(Guid businessId, string name, Guid? parentId = null, int sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("VAL_081", "Category name is required.");

        return new Category
        {
            BusinessId = businessId,
            ParentId = parentId,
            Name = name.Trim(),
            SortOrder = sortOrder
        };
    }
}

public sealed class ProductVariant : BaseEntity
{
    public Guid ProductId { get; private set; }
    public string VariantName { get; private set; } = string.Empty;
    public string Sku { get; private set; } = string.Empty;
    public string? Barcode { get; private set; }
    public decimal SellingPrice { get; private set; }
    public decimal Mrp { get; private set; }
    public decimal CurrentStock { get; private set; }
    public string? AttributesJson { get; private set; }
    public int RowVersion { get; private set; } = 1;

    private ProductVariant() { }

    public static ProductVariant Create(
        Guid businessId,
        Guid productId,
        string variantName,
        string sku,
        decimal sellingPrice,
        decimal mrp = 0m,
        decimal openingStock = 0m,
        string? barcode = null,
        string? attributesJson = null)
    {
        if (sellingPrice < 0m || openingStock < 0m)
            throw new DomainException("VAL_082", "Variant price and stock cannot be negative.");

        return new ProductVariant
        {
            BusinessId = businessId,
            ProductId = productId,
            VariantName = variantName.Trim(),
            Sku = sku.Trim().ToUpperInvariant(),
            Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim(),
            SellingPrice = sellingPrice,
            Mrp = mrp > 0m ? mrp : sellingPrice,
            CurrentStock = openingStock,
            AttributesJson = attributesJson
        };
    }
}

public sealed class ProductBatch : BaseEntity
{
    public Guid ProductId { get; private set; }
    public Guid? VariantId { get; private set; }
    public string BatchNumber { get; private set; } = string.Empty;
    public DateOnly? ManufacturedDate { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public decimal PurchasePrice { get; private set; }
    public decimal SellingPrice { get; private set; }
    public decimal Mrp { get; private set; }
    public decimal QuantityRemaining { get; private set; }

    private ProductBatch() { }

    public static ProductBatch Create(
        Guid businessId,
        Guid productId,
        string batchNumber,
        decimal purchasePrice,
        decimal sellingPrice,
        decimal quantityRemaining,
        DateOnly? manufacturedDate = null,
        DateOnly? expiryDate = null,
        decimal mrp = 0m,
        Guid? variantId = null)
    {
        if (string.IsNullOrWhiteSpace(batchNumber))
            throw new DomainException("VAL_083", "Batch number is required.");
        if (quantityRemaining < 0m)
            throw new DomainException("VAL_084", "Batch quantity cannot be negative.");

        return new ProductBatch
        {
            BusinessId = businessId,
            ProductId = productId,
            VariantId = variantId,
            BatchNumber = batchNumber.Trim().ToUpperInvariant(),
            ManufacturedDate = manufacturedDate,
            ExpiryDate = expiryDate,
            PurchasePrice = purchasePrice,
            SellingPrice = sellingPrice,
            Mrp = mrp > 0m ? mrp : sellingPrice,
            QuantityRemaining = quantityRemaining
        };
    }
}
