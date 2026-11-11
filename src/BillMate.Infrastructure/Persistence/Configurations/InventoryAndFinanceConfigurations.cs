using BillMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillMate.Infrastructure.Persistence.Configurations;

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_orders");
        builder.ConfigureBaseEntity();

        builder.Property(x => x.SupplierId).HasColumnName("supplier_id").IsRequired();
        builder.Property(x => x.PoNumber).HasColumnName("po_number").HasMaxLength(50).IsRequired();
        builder.Property(x => x.SupplierInvoiceNo).HasColumnName("supplier_invoice_no").HasMaxLength(80);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.OrderDate).HasColumnName("order_date").IsRequired();
        builder.Property(x => x.ReceivedDate).HasColumnName("received_date");
        builder.Property(x => x.TaxableAmount).HasColumnName("taxable_amount").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.TaxTotal).HasColumnName("tax_total").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.GrandTotal).HasColumnName("grand_total").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes");

        builder.HasIndex(x => new { x.BusinessId, x.PoNumber }).IsUnique();
        builder.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PurchaseOrderItemConfiguration : IEntityTypeConfiguration<PurchaseOrderItem>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderItem> builder)
    {
        builder.ToTable("po_items", t => t.HasCheckConstraint("chk_po_items_qty_pos", "ordered_qty > 0 AND received_qty >= 0"));
        builder.ConfigureBaseEntity();

        builder.Property(x => x.PurchaseOrderId).HasColumnName("purchase_order_id").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(x => x.VariantId).HasColumnName("variant_id");
        builder.Property(x => x.OrderedQty).HasColumnName("ordered_qty").HasPrecision(10, 3).IsRequired();
        builder.Property(x => x.ReceivedQty).HasColumnName("received_qty").HasPrecision(10, 3).IsRequired();
        builder.Property(x => x.UnitCost).HasColumnName("unit_cost").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.TaxAmount).HasColumnName("tax_amount").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.LineTotal).HasColumnName("line_total").HasPrecision(12, 2).IsRequired();

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements");
        builder.ConfigureBaseEntity();

        builder.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(x => x.VariantId).HasColumnName("variant_id");
        builder.Property(x => x.BatchId).HasColumnName("batch_id");
        builder.Property(x => x.MovementType).HasColumnName("movement_type").HasConversion<string>().HasMaxLength(25).IsRequired();
        builder.Property(x => x.QuantityDelta).HasColumnName("quantity_delta").HasPrecision(10, 3).IsRequired();
        builder.Property(x => x.BalanceAfter).HasColumnName("balance_after").HasPrecision(10, 3).IsRequired();
        builder.Property(x => x.ReferenceType).HasColumnName("reference_type").HasMaxLength(30);
        builder.Property(x => x.ReferenceId).HasColumnName("reference_id");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(250);
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();

        builder.HasIndex(x => new { x.BusinessId, x.ProductId, x.CreatedAt });
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.ToTable("expense_categories");
        builder.ConfigureBaseEntity();

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(250);
        builder.HasIndex(x => new { x.BusinessId, x.Name }).IsUnique();
    }
}

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("expenses", t => t.HasCheckConstraint("chk_expenses_amount_pos", "amount > 0"));
        builder.ConfigureBaseEntity();

        builder.Property(x => x.ExpenseCategoryId).HasColumnName("expense_category_id").IsRequired();
        builder.Property(x => x.SupplierId).HasColumnName("supplier_id");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.PaymentMode).HasColumnName("payment_mode").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ExpenseDate).HasColumnName("expense_date").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(300).IsRequired();
        builder.Property(x => x.ReceiptImagePath).HasColumnName("receipt_image_path").HasMaxLength(500);

        builder.HasOne<ExpenseCategory>().WithMany().HasForeignKey(x => x.ExpenseCategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LoyaltyTransactionConfiguration : IEntityTypeConfiguration<LoyaltyTransaction>
{
    public void Configure(EntityTypeBuilder<LoyaltyTransaction> builder)
    {
        builder.ToTable("loyalty_transactions", t => t.HasCheckConstraint("chk_loyalty_balance_non_neg", "balance_after >= 0"));
        builder.ConfigureBaseEntity();

        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.InvoiceId).HasColumnName("invoice_id");
        builder.Property(x => x.PointsDelta).HasColumnName("points_delta").IsRequired();
        builder.Property(x => x.BalanceAfter).HasColumnName("balance_after").IsRequired();
        builder.Property(x => x.TransactionType).HasColumnName("transaction_type").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(200);

        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}
