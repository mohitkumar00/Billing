using BillMate.Domain.Exceptions;
using BillMate.Domain.ValueObjects;

namespace BillMate.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void NonNegative_ThrowsDomainException_WhenNegative()
    {
        var ex = Assert.Throws<DomainException>(() => Money.NonNegative(-0.01m, "Price"));
        Assert.Equal("VAL_001", ex.ErrorCode);
    }

    [Theory]
    [InlineData(100.49, 100.00, -0.49)]
    [InlineData(100.50, 101.00, 0.50)]
    [InlineData(100.00, 100.00, 0.00)]
    public void RoundToNearestRupee_ComputesExpectedDelta(decimal input, decimal expectedTotal, decimal expectedDelta)
    {
        var (rounded, delta) = new Money(input).RoundToNearestRupee();
        Assert.Equal(expectedTotal, rounded.Amount);
        Assert.Equal(expectedDelta, delta);
    }

    [Theory]
    [InlineData(0, "Rupees Zero Only")]
    [InlineData(125.50, "Rupees One Hundred Twenty Five and Fifty Paise Only")]
    [InlineData(125000, "Rupees One Lakh Twenty Five Thousand Only")]
    [InlineData(20500405.75, "Rupees Two Crore Five Lakh Four Hundred Five and Seventy Five Paise Only")]
    public void ToIndianWords_FormatsCorrectly(decimal amount, string expected)
    {
        Assert.Equal(expected, new Money(amount).ToIndianWords());
    }
}
