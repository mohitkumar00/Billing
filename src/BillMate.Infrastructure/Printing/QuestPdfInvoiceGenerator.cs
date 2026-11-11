using BillMate.Application.Common.Interfaces;
using BillMate.Domain.Enums;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BillMate.Infrastructure.Printing;

public sealed class QuestPdfInvoiceGenerator : IInvoicePdfGenerator
{
    static QuestPdfInvoiceGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GenerateInvoicePdf(InvoicePdfModel model, InvoicePageFormat format = InvoicePageFormat.A4)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                if (format == InvoicePageFormat.Thermal80mm)
                {
                    page.ContinuousSize(80, Unit.Millimetre);
                    page.Margin(4, Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontSize(8));
                    ComposeThermalPage(page, model);
                    return;
                }

                page.Size(format == InvoicePageFormat.A5 ? PageSizes.A5 : PageSizes.A4);
                page.Margin(format == InvoicePageFormat.A5 ? 18 : 28);
                page.DefaultTextStyle(x => x.FontSize(format == InvoicePageFormat.A5 ? 8.5f : 9.5f));
                ComposeStandardPage(page, model);
            });
        }).GeneratePdf();
    }

    private static void ComposeStandardPage(PageDescriptor page, InvoicePdfModel m)
    {
        var title = m.InvoiceType switch
        {
            InvoiceType.BillOfSupply => "BILL OF SUPPLY",
            InvoiceType.Proforma => "PROFORMA INVOICE",
            InvoiceType.Quotation => "QUOTATION",
            InvoiceType.Challan => "DELIVERY CHALLAN",
            _ => "TAX INVOICE"
        };

        page.Header().Column(col =>
        {
            col.Item().Row(row =>
            {
                if (!string.IsNullOrWhiteSpace(m.LogoPath) && File.Exists(m.LogoPath))
                {
                    row.ConstantItem(52).Height(52).Image(m.LogoPath).FitArea();
                    row.ConstantItem(10);
                }

                row.RelativeItem().Column(biz =>
                {
                    biz.Item().Text(m.BusinessName).Bold().FontSize(14).FontColor(Colors.Blue.Darken3);
                    biz.Item().Text(m.BusinessAddress).FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                    biz.Item().Text($"Phone: {m.BusinessPhone}" + (string.IsNullOrWhiteSpace(m.BusinessGstin) ? "" : $"  |  GSTIN: {m.BusinessGstin}")).FontSize(8.5f);
                });

                row.ConstantItem(165).AlignRight().Column(inv =>
                {
                    inv.Item().Text(title).Bold().FontSize(13).FontColor(Colors.Blue.Darken3);
                    inv.Item().Text($"No: {m.InvoiceNumber}").Bold();
                    inv.Item().Text($"Date: {m.InvoiceDate.ToOffset(TimeSpan.FromHours(5.5)):dd/MM/yyyy hh:mm tt}").FontSize(8.5f);
                    inv.Item().Text($"Place of Supply: {m.PlaceOfSupplyCode}").FontSize(8.5f);
                });
            });

            col.Item().PaddingVertical(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
        });

        page.Content().Column(col =>
        {
            col.Item().PaddingBottom(6).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Bill To:").Bold().FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                    c.Item().Text(string.IsNullOrWhiteSpace(m.CustomerName) ? "Walk-in / Cash Customer" : m.CustomerName).Bold();
                    if (!string.IsNullOrWhiteSpace(m.CustomerPhone)) c.Item().Text($"Phone: {m.CustomerPhone}").FontSize(8.5f);
                    if (!string.IsNullOrWhiteSpace(m.CustomerGstin)) c.Item().Text($"GSTIN: {m.CustomerGstin}").FontSize(8.5f);
                    if (!string.IsNullOrWhiteSpace(m.CustomerAddress)) c.Item().Text(m.CustomerAddress).FontSize(8.5f);
                });

                if (!string.IsNullOrWhiteSpace(m.EwayBillNumber) || !string.IsNullOrWhiteSpace(m.VehicleNumber))
                {
                    row.ConstantItem(160).AlignRight().Column(tr =>
                    {
                        if (!string.IsNullOrWhiteSpace(m.EwayBillNumber)) tr.Item().Text($"e-Way Bill: {m.EwayBillNumber}").FontSize(8.5f);
                        if (!string.IsNullOrWhiteSpace(m.VehicleNumber)) tr.Item().Text($"Vehicle No: {m.VehicleNumber}").FontSize(8.5f);
                    });
                }
            });

            if (m.IsCompositionScheme)
            {
                col.Item().PaddingBottom(6).Background(Colors.Amber.Lighten5).Padding(4)
                    .Text("Composition taxable person, not eligible to collect tax on supplies.").Italic().FontSize(8);
            }

            // Items Table
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(22);
                    cols.RelativeColumn(3);
                    cols.ConstantColumn(48);
                    cols.ConstantColumn(44);
                    cols.ConstantColumn(52);
                    cols.ConstantColumn(56);
                    if (!m.IsCompositionScheme) cols.ConstantColumn(62);
                    cols.ConstantColumn(62);
                });

                table.Header(header =>
                {
                    static void HeaderCell(IContainer cell, string text, bool right = false)
                    {
                        var c = cell.Background(Colors.Blue.Darken3).Padding(4);
                        var t = right ? c.AlignRight().Text(text) : c.Text(text);
                        t.Bold().FontSize(8).FontColor(Colors.White);
                    }

                    HeaderCell(header.Cell(), "#");
                    HeaderCell(header.Cell(), "Item");
                    HeaderCell(header.Cell(), "HSN");
                    HeaderCell(header.Cell(), "Qty", true);
                    HeaderCell(header.Cell(), "Rate", true);
                    HeaderCell(header.Cell(), "Taxable", true);
                    if (!m.IsCompositionScheme) HeaderCell(header.Cell(), m.IsInterState ? "IGST" : "CGST+SGST", true);
                    HeaderCell(header.Cell(), "Total", true);
                });

                for (var idx = 0; idx < m.Items.Count; idx++)
                {
                    var item = m.Items[idx];
                    var bg = idx % 2 == 1 ? Colors.Grey.Lighten5 : Colors.White;

                    table.Cell().Background(bg).Padding(4).Text((idx + 1).ToString()).FontSize(8);
                    table.Cell().Background(bg).Padding(4).Text(item.ItemName).FontSize(8.5f);
                    table.Cell().Background(bg).Padding(4).Text(item.HsnSacCode ?? "-").FontSize(8);
                    table.Cell().Background(bg).Padding(4).AlignRight().Text($"{item.Quantity:0.###} {item.Unit}").FontSize(8);
                    table.Cell().Background(bg).Padding(4).AlignRight().Text($"{item.UnitPrice:N2}").FontSize(8);
                    table.Cell().Background(bg).Padding(4).AlignRight().Text($"{item.TaxableAmount:N2}").FontSize(8);

                    if (!m.IsCompositionScheme)
                    {
                        var taxAmt = m.IsInterState ? item.IgstAmount : item.CgstAmount + item.SgstAmount;
                        var taxRate = m.IsInterState ? item.IgstRate : item.CgstRate + item.SgstRate;
                        table.Cell().Background(bg).Padding(4).AlignRight().Text($"{taxAmt:N2} ({taxRate:0.#}%)").FontSize(7.5f);
                    }

                    table.Cell().Background(bg).Padding(4).AlignRight().Text($"{item.LineTotal:N2}").Bold().FontSize(8.5f);
                }
            });

            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("Amount in Words:").Bold().FontSize(8);
                    left.Item().Text(m.AmountInWords).FontSize(8.5f);

                    if (!string.IsNullOrWhiteSpace(m.UpiVpa) && m.GrandTotal > 0m)
                    {
                        var qrBytes = GenerateUpiQrPng(m.UpiVpa, m.BusinessName, m.AmountDue > 0 ? m.AmountDue : m.GrandTotal, m.InvoiceNumber);
                        left.Item().PaddingTop(8).Row(qrRow =>
                        {
                            qrRow.ConstantItem(68).Height(68).Image(qrBytes);
                            qrRow.ConstantItem(8);
                            qrRow.RelativeItem().AlignMiddle().Column(u =>
                            {
                                u.Item().Text("Scan & Pay via UPI").Bold().FontSize(8);
                                u.Item().Text(m.UpiVpa).FontSize(7.5f).FontColor(Colors.Grey.Darken2);
                            });
                        });
                    }
                });

                row.ConstantItem(200).Table(totals =>
                {
                    totals.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.ConstantColumn(80);
                    });

                    static void TotalRow(TableDescriptor t, string label, decimal val, bool bold = false)
                    {
                        var l = t.Cell().PaddingVertical(2).Text(label);
                        var v = t.Cell().PaddingVertical(2).AlignRight().Text($"₹{val:N2}");
                        if (bold) { l.Bold(); v.Bold(); }
                    }

                    TotalRow(totals, "Taxable Value", m.TaxableAmount);
                    if (!m.IsCompositionScheme)
                    {
                        if (m.IsInterState) TotalRow(totals, "IGST", m.IgstTotal);
                        else
                        {
                            TotalRow(totals, "CGST", m.CgstTotal);
                            TotalRow(totals, "SGST", m.SgstTotal);
                        }
                        if (m.CessTotal > 0m) TotalRow(totals, "Cess", m.CessTotal);
                    }
                    if (m.RoundOff != 0m) TotalRow(totals, "Round Off", m.RoundOff);
                    TotalRow(totals, "Grand Total", m.GrandTotal, bold: true);
                    TotalRow(totals, "Paid", m.AmountPaid);
                    TotalRow(totals, "Balance Due", m.AmountDue, bold: true);
                });
            });
        });

        page.Footer().Row(f =>
        {
            f.RelativeItem().Text(m.Notes ?? "Thank you for your business!").FontSize(8).FontColor(Colors.Grey.Darken2);
            f.ConstantItem(150).AlignRight().Text($"For {m.BusinessName}\nAuthorized Signatory").FontSize(8);
        });
    }

    private static void ComposeThermalPage(PageDescriptor page, InvoicePdfModel m)
    {
        page.Content().Column(col =>
        {
            col.Item().AlignCenter().Text(m.BusinessName).Bold().FontSize(10);
            col.Item().AlignCenter().Text(m.BusinessAddress).FontSize(7);
            if (!string.IsNullOrWhiteSpace(m.BusinessGstin))
                col.Item().AlignCenter().Text($"GSTIN: {m.BusinessGstin}").FontSize(7);
            col.Item().PaddingVertical(3).LineHorizontal(0.5f);
            col.Item().Text($"Bill: {m.InvoiceNumber}  {m.InvoiceDate.ToOffset(TimeSpan.FromHours(5.5)):dd/MM/yy HH:mm}").FontSize(7.5f);
            if (!string.IsNullOrWhiteSpace(m.CustomerName))
                col.Item().Text($"Cust: {m.CustomerName}").FontSize(7.5f);
            col.Item().PaddingVertical(2).LineHorizontal(0.5f);

            foreach (var it in m.Items)
            {
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text($"{it.ItemName} ({it.Quantity:0.###}x{it.UnitPrice:0.00})").FontSize(7.5f);
                    r.ConstantItem(46).AlignRight().Text($"{it.LineTotal:0.00}").FontSize(7.5f);
                });
            }

            col.Item().PaddingVertical(2).LineHorizontal(0.5f);
            col.Item().Row(r =>
            {
                r.RelativeItem().Text("TOTAL").Bold().FontSize(9.5f);
                r.ConstantItem(60).AlignRight().Text($"₹{m.GrandTotal:N2}").Bold().FontSize(9.5f);
            });
            if (!m.IsCompositionScheme)
                col.Item().Text($"Incl. GST: ₹{m.CgstTotal + m.SgstTotal + m.IgstTotal:N2}").FontSize(7);
            col.Item().PaddingTop(4).AlignCenter().Text("Thank You! Visit Again").FontSize(7.5f);
        });
    }

    public static byte[] GenerateUpiQrPng(string upiVpa, string payeeName, decimal amount, string invoiceNumber)
    {
        var uri = $"upi://pay?pa={Uri.EscapeDataString(upiVpa)}&pn={Uri.EscapeDataString(payeeName)}&am={amount:0.00}&cu=INR&tn={Uri.EscapeDataString($"Bill {invoiceNumber}")}";
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        var qr = new PngByteQRCode(data);
        return qr.GetGraphic(6);
    }
}
