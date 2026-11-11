using BillMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillMate.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers", t => t.HasCheckConstraint("chk_customers_non_neg", "credit_limit >= 0 AND loyalty_points >= 0"));
        builder.ConfigureBaseEntity();

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(180).IsRequired();
        builder.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(150);
        builder.Property(x => x.Gstin).HasColumnName("gstin").HasMaxLength(15);
        builder.Property(x => x.BillingAddress).HasColumnName("billing_address");
        builder.Property(x => x.ShippingAddress).HasColumnName("shipping_address");
        builder.Property(x => x.State).HasColumnName("state").HasMaxLength(100);
        builder.Property(x => x.StateCode).HasColumnName("state_code").HasColumnType("char(2)");
        builder.Property(x => x.PricingTier).HasColumnName("pricing_tier").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.CreditLimit).HasColumnName("credit_limit").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.OutstandingBalance).HasColumnName("outstanding_balance").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.LoyaltyPoints).HasColumnName("loyalty_points").IsRequired();

        builder.HasIndex(x => new { x.BusinessId, x.Phone }).IsUnique();
        builder.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");
        builder.ConfigureBaseEntity();

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(180).IsRequired();
        builder.Property(x => x.ContactPerson).HasColumnName("contact_person").HasMaxLength(150);
        builder.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(150);
        builder.Property(x => x.Gstin).HasColumnName("gstin").HasMaxLength(15);
        builder.Property(x => x.Address).HasColumnName("address");
        builder.Property(x => x.State).HasColumnName("state").HasMaxLength(100);
        builder.Property(x => x.StateCode).HasColumnName("state_code").HasColumnType("char(2)");
        builder.Property(x => x.PaymentTermsDays).HasColumnName("payment_terms_days").IsRequired();
        builder.Property(x => x.OutstandingPayable).HasColumnName("outstanding_payable").HasPrecision(12, 2).IsRequired();

        builder.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", t =>
        {
            t.HasCheckConstraint("chk_invoices_amounts_non_neg", "subtotal >= 0 AND discount_total >= 0 AND taxable_amount >= 0 AND grand_total >= 0 AND amount_paid >= 0");
        });
        builder.ConfigureBaseEntity();

        builder.Property(x => x.CustomerId).HasColumnName("customer_id");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.DeviceId).HasColumnName("device_id");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired();
        builder.Property(x => x.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(50).IsRequired();
        builder.Property(x => x.FinancialYear).HasColumnName("financial_year").HasMaxLength(9).IsRequired();
        builder.Property(x => x.InvoiceType).HasColumnName("invoice_type").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.InvoiceDate).HasColumnName("invoice_date").IsRequired();
        builder.Property(x => x.DueDate).HasColumnName("due_date");
        builder.Property(x => x.PlaceOfSupplyCode).HasColumnName("place_of_supply_code").HasColumnType("char(2)").IsRequired();
        builder.Property(x => x.IsInterState).HasColumnName("is_inter_state").IsRequired();
        builder.Property(x => x.IsCompositionScheme).HasColumnName("is_composition_scheme").IsRequired();
        builder.Property(x => x.IsReverseCharge).HasColumnName("is_reverse_charge").IsRequired();
        builder.Property(x => x.EwayBillNumber).HasColumnName("eway_bill_number").HasMaxLength(20);
        builder.Property(x => x.VehicleNumber).HasColumnName("vehicle_number").HasMaxLength(20);
        builder.Property(x => x.Subtotal).HasColumnName("subtotal").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.DiscountTotal).HasColumnName("discount_total").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.TaxableAmount).HasColumnName("taxable_amount").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.CgstTotal).HasColumnName("cgst_total").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.SgstTotal).HasColumnName("sgst_total").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.IgstTotal).HasColumnName("igst_total").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.CessTotal).HasColumnName("cess_total").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.RoundOff).HasColumnName("round_off").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.GrandTotal).HasColumnName("grand_total").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.AmountPaid).HasColumnName("amount_paid").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes");
        builder.Property(x => x.TermsConditions).HasColumnName("terms_conditions");
        builder.Property(x => x.PdfPath).HasColumnName("pdf_path").HasMaxLength(500);
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsConcurrencyToken().IsRequired();
        builder.Ignore(x => x.AmountDue);

        builder.HasIndex(x => new { x.BusinessId, x.FinancialYear, x.InvoiceNumber }).IsUnique();
        builder.HasIndex(x => new { x.BusinessId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.BusinessId, x.FinancialYear, x.InvoiceDate }).HasFilter("is_deleted = FALSE");
        builder.HasIndex(x => new { x.BusinessId, x.CustomerId }).HasFilter("is_deleted = FALSE");

        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Payments).WithOne().HasForeignKey(p => p.InvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> builder)
    {
        builder.ToTable("invoice_items", t => t.HasCheckConstraint("chk_invoice_items_qty_pos", "quantity > 0 AND unit_price >= 0 AND line_total >= 0"));
        builder.ConfigureBaseEntity();

        builder.Property(x => x.InvoiceId).HasColumnName("invoice_id").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(x => x.VariantId).HasColumnName("variant_id");
        builder.Property(x => x.BatchId).HasColumnName("batch_id");
        builder.Property(x => x.ItemName).HasColumnName("item_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.HsnSacCode).HasColumnName("hsn_sac_code").HasMaxLength(10);
        builder.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(10, 3).IsRequired();
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.DiscountAmount).HasColumnName("discount_amount").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.TaxableAmount).HasColumnName("taxable_amount").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.CgstRate).HasColumnName("cgst_rate").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.CgstAmount).HasColumnName("cgst_amount").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.SgstRate).HasColumnName("sgst_rate").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.SgstAmount).HasColumnName("sgst_amount").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.IgstRate).HasColumnName("igst_rate").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.IgstAmount).HasColumnName("igst_amount").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.CessRate).HasColumnName("cess_rate").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.CessAmount).HasColumnName("cess_amount").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.LineTotal).HasColumnName("line_total").HasPrecision(12, 2).IsRequired();

        builder.HasIndex(x => x.InvoiceId).HasFilter("is_deleted = FALSE");
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", t => t.HasCheckConstraint("chk_payments_amount_pos", "amount > 0"));
        builder.ConfigureBaseEntity();

        builder.Property(x => x.InvoiceId).HasColumnName("invoice_id");
        builder.Property(x => x.CustomerId).HasColumnName("customer_id");
        builder.Property(x => x.ReceivedByUserId).HasColumnName("received_by_user_id").IsRequired();
        builder.Property(x => x.PaymentMode).HasColumnName("payment_mode").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.ReferenceNumber).HasColumnName("reference_number").HasMaxLength(100);
        builder.Property(x => x.PaymentDate).HasColumnName("payment_date").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(300);

        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ReceivedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.ToTable("credit_notes", t => t.HasCheckConstraint("chk_credit_notes_non_neg", "taxable_amount >= 0 AND total_amount >= 0"));
        builder.ConfigureBaseEntity();

        builder.Property(x => x.OriginalInvoiceId).HasColumnName("original_invoice_id").IsRequired();
        builder.Property(x => x.CustomerId).HasColumnName("customer_id");
        builder.Property(x => x.CreditNoteNumber).HasColumnName("credit_note_number").HasMaxLength(50).IsRequired();
        builder.Property(x => x.FinancialYear).HasColumnName("financial_year").HasMaxLength(9).IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(300).IsRequired();
        builder.Property(x => x.TaxableAmount).HasColumnName("taxable_amount").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.CgstTotal).HasColumnName("cgst_total").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.SgstTotal).HasColumnName("sgst_total").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.IgstTotal).HasColumnName("igst_total").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.CessTotal).HasColumnName("cess_total").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.TotalAmount).HasColumnName("total_amount").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.IssuedAt).HasColumnName("issued_at").IsRequired();

        builder.HasIndex(x => new { x.BusinessId, x.FinancialYear, x.CreditNoteNumber }).IsUnique();
        builder.HasOne<Invoice>().WithMany().HasForeignKey(x => x.OriginalInvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}
