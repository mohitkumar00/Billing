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
}
