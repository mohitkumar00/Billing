using BillMate.Domain.Exceptions;

namespace BillMate.Domain.ValueObjects;

/// <summary>
/// Indian Financial Year (April 1 to March 31), e.g. "2026-2027".
/// </summary>
public readonly record struct FinancialYear
{
    public int StartYear { get; }
    public int EndYear => StartYear + 1;
    public string Code => $"{StartYear}-{EndYear}";

    public DateOnly StartDate => new(StartYear, 4, 1);
    public DateOnly EndDate => new(EndYear, 3, 31);

    private FinancialYear(int startYear)
    {
        if (startYear < 2000 || startYear > 2100)
            throw new DomainException("VAL_004", $"Invalid financial year start: {startYear}.");
        StartYear = startYear;
    }

    public static FinancialYear FromDate(DateTimeOffset dateUtcOrIst)
    {
        // Convert to IST (+05:30) for Indian FY rollover accuracy at midnight IST
        var istOffset = TimeSpan.FromHours(5.5);
        var istDate = dateUtcOrIst.ToOffset(istOffset);
        var startYear = istDate.Month >= 4 ? istDate.Year : istDate.Year - 1;
        return new FinancialYear(startYear);
    }

    public static FinancialYear FromStartYear(int startYear) => new(startYear);

    public string FormatSequenceNumber(string prefix, int sequenceNumber)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new DomainException("VAL_005", "Invoice prefix cannot be empty.");
        if (sequenceNumber <= 0)
            throw new DomainException("VAL_006", "Sequence number must be positive.");

        var shortEnd = EndYear % 100;
        return $"{prefix.Trim().ToUpperInvariant()}-{StartYear % 100:00}{shortEnd:00}-{sequenceNumber:0000}";
    }

    public override string ToString() => Code;
}
