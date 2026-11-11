namespace BillMate.Domain.ValueObjects;

/// <summary>
/// Immutable per-line GST tax calculation breakdown.
/// </summary>
public sealed record TaxBreakdown(
    decimal TaxableAmount,
    decimal CgstRate,
    decimal CgstAmount,
    decimal SgstRate,
    decimal SgstAmount,
    decimal IgstRate,
    decimal IgstAmount,
    decimal CessRate,
    decimal CessAmount,
    decimal LineTotal)
{
    public decimal TotalTax => CgstAmount + SgstAmount + IgstAmount + CessAmount;
}
