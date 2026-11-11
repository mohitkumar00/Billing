using BillMate.Domain.Entities;
using BillMate.Domain.Enums;
using BillMate.Domain.Exceptions;

namespace BillMate.Domain.Tests;

public class InvoiceAndStockTests
{
    [Fact]
    public void InvoiceLifecycle_ConfirmPayAndCancelCreatesCreditNote()
    {
        var businessId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var invoice = Invoice.CreateDraft(
            businessId: businessId,
            createdByUserId: userId,
            idempotencyKey: Guid.NewGuid(),
            invoiceNumber: "INV-2627-0001",
            sellerStateCode: "27",
            placeOfSupplyCode: "27",
            isCompositionScheme: false);

        invoice.AddItem(
            productId: Guid.NewGuid(),
            itemName: "Tata Salt 1kg",
            unit: "PCS",
            quantity: 2m,
            unitPrice: 30m,
            discountAmount: 0m,
            gstRatePercent: 5m,
            cessRatePercent: 0m,
            isTaxInclusive: false);

        invoice.Confirm();
        Assert.Equal(InvoiceStatus.Confirmed, invoice.Status);
        Assert.Equal(63m, invoice.GrandTotal);

        // Immutability check after confirm
        var editEx = Assert.Throws<DomainException>(() =>
            invoice.AddItem(Guid.NewGuid(), "Extra", "PCS", 1m, 10m, 0m, 0m, 0m, false));
        Assert.Equal("BILL_002", editEx.ErrorCode);

        // Partial then Full Payment
        invoice.RecordPayment(userId, PaymentMode.Cash, 40m);
        Assert.Equal(InvoiceStatus.Partial, invoice.Status);
        Assert.Equal(23m, invoice.AmountDue);

        invoice.RecordPayment(userId, PaymentMode.Upi, 23m, "UTR123456");
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(0m, invoice.AmountDue);

        // Cancel & Credit Note
        var creditNote = invoice.Cancel("Customer returned items", "CN-2627-0001");
        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
        Assert.Equal(63m, creditNote.TotalAmount);
        Assert.Equal(1.50m, creditNote.CgstTotal);
        Assert.Equal(1.50m, creditNote.SgstTotal);
    }

    [Fact]
    public void CompositeBusiness_CreatesBillOfSupply()
    {
        var invoice = Invoice.CreateDraft(
            businessId: Guid.NewGuid(),
            createdByUserId: Guid.NewGuid(),
            idempotencyKey: Guid.NewGuid(),
            invoiceNumber: "BOS-2627-0001",
            sellerStateCode: "27",
            placeOfSupplyCode: "27",
            isCompositionScheme: true);

        invoice.AddItem(Guid.NewGuid(), "Rice 5kg", "BAG", 1m, 350m, 0m, 5m, 0m, false);
        invoice.Confirm();

        Assert.Equal(InvoiceType.BillOfSupply, invoice.InvoiceType);
        Assert.Equal(0m, invoice.CgstTotal + invoice.SgstTotal + invoice.IgstTotal);
        Assert.Equal(350m, invoice.GrandTotal);
    }

    [Fact]
    public void Product_DeductStock_ThrowsWhenInsufficient_AndBypassedForService()
    {
        var businessId = Guid.NewGuid();
        var slabId = Guid.NewGuid();

        var goods = Product.Create(businessId, slabId, "SKU-001", "Soap", 40m, openingStock: 3m);
        Assert.Equal(1m, goods.DeductStock(2m));

        var stockEx = Assert.Throws<InsufficientStockException>(() => goods.DeductStock(2m));
        Assert.Equal("INV_002", stockEx.ErrorCode);

        var service = Product.Create(businessId, slabId, "SRV-001", "Repair", 500m, isService: true);
        Assert.Equal(0m, service.DeductStock(10m));
    }

    [Fact]
    public void Customer_EnforcesCreditLimit_AndTracksLoyalty()
    {
        var customer = Customer.Create(Guid.NewGuid(), "Ramesh Traders", "9876543210", creditLimit: 1000m);
        customer.AddCreditSale(800m);
        Assert.Equal(800m, customer.OutstandingBalance);

        var creditEx = Assert.Throws<DomainException>(() => customer.AddCreditSale(300m));
        Assert.Equal("CRM_001", creditEx.ErrorCode);

        customer.EarnLoyaltyPoints(50);
        customer.RedeemLoyaltyPoints(20);
        Assert.Equal(30, customer.LoyaltyPoints);
    }
}
