using System.Text;
using BillMate.Application.Common.Interfaces;
using BillMate.Domain.Enums;

namespace BillMate.Infrastructure.Printing;

/// <summary>
/// Pure ESC/POS byte stream generator for 58mm/80mm thermal printers and RJ12 cash drawer kick.
/// </summary>
public sealed class EscPosReceiptBuilder : IEscPosReceiptBuilder
{
    private static readonly byte[] InitPrinter = [0x1B, 0x40];                 // ESC @
    private static readonly byte[] AlignLeft = [0x1B, 0x61, 0x00];             // ESC a 0
    private static readonly byte[] AlignCenter = [0x1B, 0x61, 0x01];           // ESC a 1
    private static readonly byte[] BoldOn = [0x1B, 0x45, 0x01];                // ESC E 1
    private static readonly byte[] BoldOff = [0x1B, 0x45, 0x00];               // ESC E 0
    private static readonly byte[] PartialCut = [0x1D, 0x56, 0x42, 0x03];      // GS V 66 3
    private static readonly byte[] CashDrawerKick = [0x1B, 0x70, 0x00, 0x19, 0xFA]; // ESC p 0 25 250

    public byte[] BuildCashDrawerKickCommand() => (byte[])CashDrawerKick.Clone();

    public byte[] BuildReceipt(InvoicePdfModel model, int paperWidthMm = 80, bool openCashDrawer = false)
    {
        var cols = paperWidthMm <= 58 ? 32 : 48;
        var sep = new string('-', cols);
        var buf = new List<byte>(1024);

        void AppendText(string line) => buf.AddRange(Encoding.ASCII.GetBytes(line + "\n"));

        buf.AddRange(InitPrinter);
        buf.AddRange(AlignCenter);
        buf.AddRange(BoldOn);
        AppendText(Truncate(model.BusinessName, cols));
        buf.AddRange(BoldOff);
        AppendText(Truncate(model.BusinessAddress, cols));
        AppendText($"Ph: {model.BusinessPhone}");
        if (!string.IsNullOrWhiteSpace(model.BusinessGstin))
            AppendText($"GSTIN: {model.BusinessGstin}");

        var docLabel = model.InvoiceType == InvoiceType.BillOfSupply ? "BILL OF SUPPLY" : "TAX INVOICE";
        buf.AddRange(BoldOn);
        AppendText(docLabel);
        buf.AddRange(BoldOff);

        buf.AddRange(AlignLeft);
        AppendText(sep);
        AppendText($"Bill: {model.InvoiceNumber}");
        AppendText($"Date: {model.InvoiceDate.ToOffset(TimeSpan.FromHours(5.5)):dd/MM/yyyy HH:mm}");
        if (!string.IsNullOrWhiteSpace(model.CustomerName))
            AppendText($"Cust: {Truncate(model.CustomerName, cols - 6)}");
        AppendText(sep);

        foreach (var item in model.Items)
        {
            AppendText(Truncate(item.ItemName, cols));
            var left = $" {item.Quantity:0.###} {item.Unit} x {item.UnitPrice:0.00}";
            var right = $"{item.LineTotal:0.00}";
            AppendText(FormatColumns(left, right, cols));
        }

        AppendText(sep);
        if (!model.IsCompositionScheme)
        {
            AppendText(FormatColumns("Taxable:", $"{model.TaxableAmount:0.00}", cols));
            if (model.IsInterState)
                AppendText(FormatColumns("IGST:", $"{model.IgstTotal:0.00}", cols));
            else
            {
                AppendText(FormatColumns("CGST:", $"{model.CgstTotal:0.00}", cols));
                AppendText(FormatColumns("SGST:", $"{model.SgstTotal:0.00}", cols));
            }
        }
        else
        {
            AppendText("Composition Dealer - No GST Charged");
        }

        buf.AddRange(BoldOn);
        AppendText(FormatColumns("GRAND TOTAL:", $"Rs.{model.GrandTotal:0.00}", cols));
        buf.AddRange(BoldOff);
        AppendText(FormatColumns("Paid:", $"Rs.{model.AmountPaid:0.00}", cols));
        if (model.AmountDue > 0m)
            AppendText(FormatColumns("Balance Due:", $"Rs.{model.AmountDue:0.00}", cols));

        AppendText(sep);
        buf.AddRange(AlignCenter);
        AppendText("Thank You! Visit Again");
        AppendText("\n");

        buf.AddRange(PartialCut);
        if (openCashDrawer)
            buf.AddRange(CashDrawerKick);

        return buf.ToArray();
    }

    private static string FormatColumns(string left, string right, int width)
    {
        var spaces = Math.Max(1, width - left.Length - right.Length);
        return left.Length + right.Length >= width
            ? $"{Truncate(left, width - right.Length - 1)} {right}"
            : $"{left}{new string(' ', spaces)}{right}";
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
