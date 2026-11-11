using BillMate.Domain.Exceptions;
using BillMate.Domain.ValueObjects;

namespace BillMate.Domain.Services;

/// <summary>
/// Pure Indian GST calculation engine supporting intra-state (CGST+SGST),
/// inter-state (IGST), Cess, tax-inclusive pricing, and Composite Scheme (Bill of Supply).
/// </summary>
public static class GstCalculator
{
    public static TaxBreakdown CalculateLine(
        decimal quantity,
        decimal unitPrice,
        decimal discountAmount,
        decimal gstRatePercent,
        decimal cessRatePercent,
        bool isTaxInclusive,
        bool isInterState,
        bool isCompositionScheme)
    {
        if (quantity <= 0m)
            throw new DomainException("VAL_010", "Quantity must be greater than zero.");
        if (unitPrice < 0m)
            throw new DomainException("VAL_011", "Unit price cannot be negative.");
        if (discountAmount < 0m)
            throw new DomainException("VAL_012", "Discount amount cannot be negative.");
        if (gstRatePercent < 0m || cessRatePercent < 0m)
            throw new DomainException("VAL_013", "Tax rates cannot be negative.");

        var gross = Round2(quantity * unitPrice);
        if (discountAmount > gross)
            throw new DomainException("VAL_014", "Discount cannot exceed gross line amount.");

        var netInput = Round2(gross - discountAmount);

        if (isCompositionScheme)
        {
            return new TaxBreakdown(
                TaxableAmount: netInput,
                CgstRate: 0m,
                CgstAmount: 0m,
                SgstRate: 0m,
                SgstAmount: 0m,
                IgstRate: 0m,
                IgstAmount: 0m,
                CessRate: 0m,
                CessAmount: 0m,
                LineTotal: netInput);
        }

        var combinedRate = gstRatePercent + cessRatePercent;
        decimal taxable;

        if (isTaxInclusive && combinedRate > 0m)
        {
            taxable = Round2(netInput / (1m + combinedRate / 100m));
        }
        else
        {
            taxable = netInput;
        }

        decimal cgstRate = 0m, cgstAmt = 0m;
        decimal sgstRate = 0m, sgstAmt = 0m;
        decimal igstRate = 0m, igstAmt = 0m;

        if (isInterState)
        {
            igstRate = gstRatePercent;
            igstAmt = isTaxInclusive
                ? Round2(netInput * igstRate / (100m + combinedRate))
                : Round2(taxable * igstRate / 100m);
        }
        else
        {
            cgstRate = gstRatePercent / 2m;
            sgstRate = gstRatePercent / 2m;
            cgstAmt = isTaxInclusive
                ? Round2(netInput * cgstRate / (100m + combinedRate))
                : Round2(taxable * cgstRate / 100m);
            sgstAmt = isTaxInclusive
                ? Round2(netInput * sgstRate / (100m + combinedRate))
                : Round2(taxable * sgstRate / 100m);
        }

        var cessAmt = isTaxInclusive
            ? Round2(netInput * cessRatePercent / (100m + combinedRate))
            : Round2(taxable * cessRatePercent / 100m);

        // Keep Taxable + TotalTax == LineTotal invariant exact to the paisa in tax-inclusive mode
        if (isTaxInclusive)
        {
            var totalTax = cgstAmt + sgstAmt + igstAmt + cessAmt;
            taxable = Round2(netInput - totalTax);
            return new TaxBreakdown(
                TaxableAmount: taxable,
                CgstRate: cgstRate,
                CgstAmount: cgstAmt,
                SgstRate: sgstRate,
                SgstAmount: sgstAmt,
                IgstRate: igstRate,
                IgstAmount: igstAmt,
                CessRate: cessRatePercent,
                CessAmount: cessAmt,
                LineTotal: netInput);
        }

        var lineTotal = Round2(taxable + cgstAmt + sgstAmt + igstAmt + cessAmt);
        return new TaxBreakdown(
            TaxableAmount: taxable,
            CgstRate: cgstRate,
            CgstAmount: cgstAmt,
            SgstRate: sgstRate,
            SgstAmount: sgstAmt,
            IgstRate: igstRate,
            IgstAmount: igstAmt,
            CessRate: cessRatePercent,
            CessAmount: cessAmt,
            LineTotal: lineTotal);
    }

    private static decimal Round2(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
