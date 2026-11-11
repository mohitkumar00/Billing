using BillMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillMate.Infrastructure.Persistence.Configurations;

internal sealed class TaxSlabConfiguration : IEntityTypeConfiguration<TaxSlab>
{
    public void Configure(EntityTypeBuilder<TaxSlab> builder)
    {
        builder.ToTable("tax_slabs", t => t.HasCheckConstraint("chk_tax_slabs_non_neg", "total_rate >= 0 AND cess_rate >= 0"));
        builder.ConfigureBaseEntity();

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(50).IsRequired();
        builder.Property(x => x.TotalRate).HasColumnName("total_rate").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.CgstRate).HasColumnName("cgst_rate").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.SgstRate).HasColumnName("sgst_rate").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.IgstRate).HasColumnName("igst_rate").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.CessRate).HasColumnName("cess_rate").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.IsDefault).HasColumnName("is_default").HasDefaultValue(false).IsRequired();

        builder.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class HsnSacCodeConfiguration : IEntityTypeConfiguration<HsnSacCode>
{
    public void Configure(EntityTypeBuilder<HsnSacCode> builder)
    {
        builder.ToTable("hsn_sac_codes", t => t.HasCheckConstraint("chk_hsn_sac_type", "code_type IN ('HSN', 'SAC')"));
        builder.ConfigureBaseEntity();

        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(10).IsRequired();
        builder.Property(x => x.CodeType).HasColumnName("code_type").HasColumnType("char(3)").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(300).IsRequired();
        builder.Property(x => x.DefaultTaxSlabId).HasColumnName("default_tax_slab_id");

        builder.HasIndex(x => new { x.BusinessId, x.Code }).IsUnique();
        builder.HasOne<TaxSlab>().WithMany().HasForeignKey(x => x.DefaultTaxSlabId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.ConfigureBaseEntity();

        builder.Property(x => x.ParentId).HasColumnName("parent_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.SortOrder).HasColumnName("sort_order").HasDefaultValue(0).IsRequired();

        builder.HasOne<Category>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", t =>
        {
            t.HasCheckConstraint("chk_non_service_stock_non_negative", "is_service = TRUE OR current_stock >= 0");
            t.HasCheckConstraint("chk_products_prices_non_neg", "purchase_price >= 0 AND selling_price >= 0 AND mrp >= 0");
        });
        builder.ConfigureBaseEntity();

        builder.Property(x => x.CategoryId).HasColumnName("category_id");
        builder.Property(x => x.TaxSlabId).HasColumnName("tax_slab_id").IsRequired();
        builder.Property(x => x.HsnSacCode).HasColumnName("hsn_sac_code").HasMaxLength(10);
        builder.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Barcode).HasColumnName("barcode").HasMaxLength(80);
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.Brand).HasColumnName("brand").HasMaxLength(100);
        builder.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(20).IsRequired();
        builder.Property(x => x.IsService).HasColumnName("is_service").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.IsTaxInclusive).HasColumnName("is_tax_inclusive").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.IsRcmApplicable).HasColumnName("is_rcm_applicable").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.PurchasePrice).HasColumnName("purchase_price").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.SellingPrice).HasColumnName("selling_price").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.Mrp).HasColumnName("mrp").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.CurrentStock).HasColumnName("current_stock").HasPrecision(10, 3).IsRequired();
        builder.Property(x => x.LowStockThreshold).HasColumnName("low_stock_threshold").HasPrecision(10, 3).IsRequired();
        builder.Property(x => x.TrackBatches).HasColumnName("track_batches").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ImagePath).HasColumnName("image_path").HasMaxLength(500);
        builder.Property(x => x.IsFavourite).HasColumnName("is_favourite").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsConcurrencyToken().IsRequired();

        builder.HasIndex(x => new { x.BusinessId, x.Sku }).IsUnique();
        builder.HasIndex(x => new { x.BusinessId, x.Barcode }).HasFilter("is_deleted = FALSE AND barcode IS NOT NULL");
        builder.HasIndex(x => new { x.BusinessId, x.Name }).HasFilter("is_deleted = FALSE");
        builder.HasIndex(x => new { x.BusinessId, x.CurrentStock }).HasFilter("is_deleted = FALSE AND is_service = FALSE");

        builder.HasOne(x => x.TaxSlab).WithMany().HasForeignKey(x => x.TaxSlabId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("product_variants", t => t.HasCheckConstraint("chk_variants_stock_non_neg", "current_stock >= 0"));
        builder.ConfigureBaseEntity();

        builder.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(x => x.VariantName).HasColumnName("variant_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Barcode).HasColumnName("barcode").HasMaxLength(80);
        builder.Property(x => x.SellingPrice).HasColumnName("selling_price").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.Mrp).HasColumnName("mrp").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.CurrentStock).HasColumnName("current_stock").HasPrecision(10, 3).IsRequired();
        builder.Property(x => x.AttributesJson).HasColumnName("attributes").HasColumnType("jsonb");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsConcurrencyToken().IsRequired();

        builder.HasIndex(x => new { x.BusinessId, x.Sku }).IsUnique();
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductBatchConfiguration : IEntityTypeConfiguration<ProductBatch>
{
    public void Configure(EntityTypeBuilder<ProductBatch> builder)
    {
        builder.ToTable("product_batches", t => t.HasCheckConstraint("chk_batches_qty_non_neg", "quantity_remaining >= 0"));
        builder.ConfigureBaseEntity();

        builder.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(x => x.VariantId).HasColumnName("variant_id");
        builder.Property(x => x.BatchNumber).HasColumnName("batch_number").HasMaxLength(80).IsRequired();
        builder.Property(x => x.ManufacturedDate).HasColumnName("manufactured_date");
        builder.Property(x => x.ExpiryDate).HasColumnName("expiry_date");
        builder.Property(x => x.PurchasePrice).HasColumnName("purchase_price").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.SellingPrice).HasColumnName("selling_price").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.Mrp).HasColumnName("mrp").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.QuantityRemaining).HasColumnName("quantity_remaining").HasPrecision(10, 3).IsRequired();

        builder.HasIndex(x => new { x.BusinessId, x.ProductId, x.BatchNumber }).IsUnique();
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
    }
}
