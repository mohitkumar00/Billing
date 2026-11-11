using BillMate.Domain.Exceptions;
using BillMate.Domain.ValueObjects;

namespace BillMate.Domain.Tests;

public class GstinAndFinancialYearTests
{
    [Fact]
    public void Gstin_ValidatesAndExtractsStateCodeAndPan()
    {
        var gstin = Gstin.Create(" 27aaacp1234a1z5 ");
        Assert.Equal("27AAACP1234A1Z5", gstin.Value);
        Assert.Equal("27", gstin.StateCode);
        Assert.Equal("AAACP1234A", gstin.Pan);
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("99AAACP1234A1Z5")]
    [InlineData("27AAACP1234A1X5")]
    public void Gstin_ThrowsOnInvalidFormat(string invalid)
    {
        var ex = Assert.Throws<DomainException>(() => Gstin.Create(invalid));
        Assert.Equal("VAL_003", ex.ErrorCode);
    }

    [Fact]
    public void FinancialYear_RollsOverOnAprilFirstIst()
    {
        var march31 = new DateTimeOffset(2026, 3, 31, 23, 59, 0, TimeSpan.FromHours(5.5));
        var april1 = new DateTimeOffset(2026, 4, 1, 0, 1, 0, TimeSpan.FromHours(5.5));

        var fyBefore = FinancialYear.FromDate(march31);
        var fyAfter = FinancialYear.FromDate(april1);

        Assert.Equal("2025-2026", fyBefore.Code);
        Assert.Equal("2026-2027", fyAfter.Code);
        Assert.Equal("INV-2627-0042", fyAfter.FormatSequenceNumber("inv", 42));
    }
}
