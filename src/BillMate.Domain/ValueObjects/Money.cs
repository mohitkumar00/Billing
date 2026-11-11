using BillMate.Domain.Exceptions;

namespace BillMate.Domain.ValueObjects;

/// <summary>
/// Immutable INR monetary value rounded to 2 decimal places using AwayFromZero.
/// </summary>
public readonly record struct Money
{
    public decimal Amount { get; }

    public static Money Zero => new(0m);

    public Money(decimal amount)
    {
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    public static Money NonNegative(decimal amount, string fieldName = "Amount")
    {
        if (amount < 0m)
            throw new DomainException("VAL_001", $"{fieldName} cannot be negative.");
        return new Money(amount);
    }

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount);
    public static Money operator -(Money left, Money right) => new(left.Amount - right.Amount);
    public static Money operator *(Money money, decimal factor) => new(money.Amount * factor);
    public static bool operator >(Money left, Money right) => left.Amount > right.Amount;
    public static bool operator <(Money left, Money right) => left.Amount < right.Amount;
    public static bool operator >=(Money left, Money right) => left.Amount >= right.Amount;
    public static bool operator <=(Money left, Money right) => left.Amount <= right.Amount;

    /// <summary>
    /// Computes standard Indian invoice round-off to nearest rupee and returns (RoundedGrandTotal, RoundOffDelta).
    /// </summary>
    public (Money RoundedTotal, decimal RoundOffDelta) RoundToNearestRupee()
    {
        var rounded = decimal.Round(Amount, 0, MidpointRounding.AwayFromZero);
        var delta = decimal.Round(rounded - Amount, 2, MidpointRounding.AwayFromZero);
        return (new Money(rounded), delta);
    }

    /// <summary>
    /// Converts monetary amount into Indian numbering words (Lakhs / Crores).
    /// </summary>
    public string ToIndianWords()
    {
        var absolute = Math.Abs(Amount);
        var rupees = (long)Math.Floor(absolute);
        var paise = (int)decimal.Round((absolute - rupees) * 100m, 0, MidpointRounding.AwayFromZero);

        var rupeeWords = rupees == 0 ? "Zero" : ConvertWholeNumberToIndianWords(rupees);
        var prefix = Amount < 0 ? "Minus " : string.Empty;

        if (paise == 0)
            return $"{prefix}Rupees {rupeeWords} Only";

        var paiseWords = ConvertWholeNumberToIndianWords(paise);
        return $"{prefix}Rupees {rupeeWords} and {paiseWords} Paise Only";
    }

    private static string ConvertWholeNumberToIndianWords(long number)
    {
        string[] ones =
        [
            "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
            "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"
        ];
        string[] tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

        string TwoDigits(long n)
        {
            if (n < 20) return ones[n];
            var t = tens[n / 10];
            var o = ones[n % 10];
            return string.IsNullOrEmpty(o) ? t : $"{t} {o}";
        }

        string ThreeDigits(long n)
        {
            var hundreds = n / 100;
            var rem = n % 100;
            if (hundreds == 0) return TwoDigits(rem);
            return rem == 0 ? $"{ones[hundreds]} Hundred" : $"{ones[hundreds]} Hundred {TwoDigits(rem)}";
        }

        var parts = new List<string>();

        var crores = number / 10_000_000;
        if (crores > 0)
        {
            parts.Add($"{ConvertWholeNumberToIndianWords(crores)} Crore");
            number %= 10_000_000;
        }

        var lakhs = number / 100_000;
        if (lakhs > 0)
        {
            parts.Add($"{TwoDigits(lakhs)} Lakh");
            number %= 100_000;
        }

        var thousands = number / 1_000;
        if (thousands > 0)
        {
            parts.Add($"{TwoDigits(thousands)} Thousand");
            number %= 1_000;
        }

        if (number > 0)
        {
            parts.Add(ThreeDigits(number));
        }

        return string.Join(" ", parts);
    }

    public override string ToString() => $"₹{Amount:N2}";
}
