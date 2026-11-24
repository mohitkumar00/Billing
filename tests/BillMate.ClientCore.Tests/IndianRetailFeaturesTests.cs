using BillMate.ClientCore.Services;
using Xunit;

namespace BillMate.ClientCore.Tests;

public class IndianRetailFeaturesTests
{
    [Fact]
    public void GenerateUpiUri_GeneratesValidNpciFormat()
    {
        var uri = UpiPaymentHelper.GenerateUpiUri("merchant@icici", "Super Kirana Store", 1250.50m, "INV-2026-001");

        Assert.StartsWith("upi://pay?", uri);
        Assert.Contains("pa=merchant@icici", uri);
        Assert.Contains("pn=Super%20Kirana%20Store", uri);
        Assert.Contains("am=1250.50", uri);
        Assert.Contains("cu=INR", uri);
        Assert.Contains("tr=INV-2026-001", uri);
    }

    [Fact]
    public void BuildInvoiceShareUrl_SanitizesPhoneAndGeneratesWhatsAppUrl()
    {
        var url = WhatsAppShareHelper.BuildInvoiceShareUrl("9876543210", "Chai Point", "CP-88", 120m, 0m, 2, "cp@upi");

        Assert.StartsWith("https://wa.me/919876543210?text=", url);
        Assert.Contains("CHAI%20POINT", url);
        Assert.Contains("%23CP-88", url);
        Assert.Contains("PAID%20IN%20FULL", url);
    }

    [Fact]
    public void BuildKhataReminderUrl_IncludesUdharBalanceAndUpiLink()
    {
        var url = WhatsAppShareHelper.BuildKhataReminderUrl("919876543210", "Ramesh Kumar", "Sharma Traders", 3450m, "sharma@sbi");

        Assert.StartsWith("https://wa.me/919876543210?text=", url);
        Assert.Contains("Ramesh%20Kumar", url);
        Assert.Contains("3%2C450.00", url);
        Assert.Contains("sharma%40sbi", url);
    }

    [Fact]
    public async Task ExcelImportService_ParsesCsvStreamSuccessfully()
    {
        var csv = "Name,Barcode,SellingPrice,PurchasePrice,MRP,OpeningStock,TaxRatePercent,Unit,LowStockThreshold\n" +
                  "Tata Tea Premium,8901234567890,140.00,110.00,150.00,50,5,PCS,10\n" +
                  "Aashirvaad Atta 5kg,8901234567891,245.00,210.00,260.00,25,0,PACK,5";

        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var items = await ExcelImportService.ParseStreamAsync(ms, "products.csv");

        Assert.Equal(2, items.Count);
        Assert.Equal("Tata Tea Premium", items[0].Name);
        Assert.Equal("8901234567890", items[0].Barcode);
        Assert.Equal(140.00m, items[0].SellingPrice);
        Assert.Equal(110.00m, items[0].PurchasePrice);
        Assert.Equal(150.00m, items[0].Mrp);
        Assert.Equal(50m, items[0].OpeningStock);
        Assert.Equal(5m, items[0].TaxRatePercent);

        Assert.Equal("Aashirvaad Atta 5kg", items[1].Name);
        Assert.Equal("PACK", items[1].Unit);
    }

    [Fact]
    public void PosCartLine_WithDiscount_CalculatesSavingsCorrectly()
    {
        var prod = new Application.Features.Products.ProductDto(
            Guid.NewGuid(), "SKU-SALT", "890123", "Tata Salt", "PCS", "2501", Guid.NewGuid(),
            5m, 30m, 30m, 100m, 5m, false, true, false, 1);

        var line = new ViewModels.CartLineViewModel(prod, 2m);
        line.DiscountAmount = 10m; // ₹10 discount on 2 items (normally ₹60 total)
        line.Recalculate(false, false);

        Assert.True(line.HasDiscount);
        Assert.Equal("-₹10", line.DiscountBadgeText);
        Assert.Equal(50m, line.LineTotal);
    }
}
