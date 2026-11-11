using BillMate.Domain.Services;

namespace BillMate.Domain.Tests;

public class GstCalculatorTests
{
    [Fact]
    public void IntraStateExclusive_SplitsIntoCgstAndSgst()
    {
        var result = GstCalculator.CalculateLine(
            quantity: 2m,
            unitPrice: 500m,
            discountAmount: 0m,
            gstRatePercent: 18m,
            cessRatePercent: 0m,
            isTaxInclusive: false,
            isInterState: false,
            isCompositionScheme: false);

        Assert.Equal(1000m, result.TaxableAmount);
        Assert.Equal(9m, result.CgstRate);
        Assert.Equal(90m, result.CgstAmount);
        Assert.Equal(9m, result.SgstRate);
        Assert.Equal(90m, result.SgstAmount);
        Assert.Equal(0m, result.IgstAmount);
        Assert.Equal(1180m, result.LineTotal);
    }

    [Fact]
    public void InterStateExclusive_AppliesIgstAndCess()
    {
        var result = GstCalculator.CalculateLine(
            quantity: 1m,
            unitPrice: 1000m,
            discountAmount: 100m,
            gstRatePercent: 28m,
            cessRatePercent: 12m,
            isTaxInclusive: false,
            isInterState: true,
            isCompositionScheme: false);

        Assert.Equal(900m, result.TaxableAmount);
        Assert.Equal(0m, result.CgstAmount);
        Assert.Equal(0m, result.SgstAmount);
        Assert.Equal(28m, result.IgstRate);
        Assert.Equal(252m, result.IgstAmount);
        Assert.Equal(108m, result.CessAmount);
        Assert.Equal(1260m, result.LineTotal);
    }

    [Fact]
    public void TaxInclusive_PreservesTotalAndExtractsTax()
    {
        var result = GstCalculator.CalculateLine(
            quantity: 1m,
            unitPrice: 118m,
            discountAmount: 0m,
            gstRatePercent: 18m,
            cessRatePercent: 0m,
            isTaxInclusive: true,
            isInterState: false,
            isCompositionScheme: false);

        Assert.Equal(100m, result.TaxableAmount);
        Assert.Equal(9m, result.CgstAmount);
        Assert.Equal(9m, result.SgstAmount);
        Assert.Equal(118m, result.LineTotal);
        Assert.Equal(result.LineTotal, result.TaxableAmount + result.TotalTax);
    }

    [Fact]
    public void CompositionScheme_ProducesZeroTax()
    {
        var result = GstCalculator.CalculateLine(
            quantity: 3m,
            unitPrice: 200m,
            discountAmount: 50m,
            gstRatePercent: 18m,
            cessRatePercent: 5m,
            isTaxInclusive: false,
            isInterState: true,
            isCompositionScheme: true);

        Assert.Equal(550m, result.TaxableAmount);
        Assert.Equal(0m, result.TotalTax);
        Assert.Equal(550m, result.LineTotal);
    }
}
