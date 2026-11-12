using System.Text;
using System.Xml.Linq;
using BillMate.Application.Common.Exceptions;
using BillMate.Application.Common.Interfaces;
using BillMate.Application.Common.Security;
using BillMate.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BillMate.Application.Features.Reports;

public sealed record SalesSummaryDto(
    int InvoiceCount,
    decimal GrossSales,
    decimal TaxableTotal,
    decimal CgstTotal,
    decimal SgstTotal,
    decimal IgstTotal,
    decimal CessTotal,
    decimal CollectedTotal,
    decimal OutstandingCreditTotal);

public sealed record Gstr1B2BEntry(string CustomerGstin, string CustomerName, string InvoiceNumber, DateTimeOffset InvoiceDate, string PlaceOfSupply, decimal TaxableValue, decimal Cgst, decimal Sgst, decimal Igst, decimal Cess, decimal InvoiceValue);
public sealed record Gstr1HsnEntry(string HsnSacCode, string Unit, decimal TotalQuantity, decimal TaxableValue, decimal TotalTax);
public sealed record Gstr1ReportDto(IReadOnlyList<Gstr1B2BEntry> B2BInvoices, decimal B2CSmallTaxableTotal, decimal B2CSmallTaxTotal, IReadOnlyList<Gstr1HsnEntry> HsnSummary);

public sealed record Gstr3bSlabSummary(decimal TaxRatePercent, decimal TaxableValue, decimal Cgst, decimal Sgst, decimal Igst, decimal Cess);
public sealed record Gstr3bReportDto(decimal CompositionTurnover, IReadOnlyList<Gstr3bSlabSummary> RegularSlabs);

public sealed record DayBookTransactionDto(
    string Time,
    string Type,
    string ReferenceNumber,
    string Description,
    PaymentMode PaymentMode,
    decimal Inflow,
    decimal Outflow);

public sealed record DayBookReportDto(
    DateOnly Date,
    int TotalInvoices,
    decimal TotalSales,
    decimal CashSales,
    decimal UpiSales,
    decimal CardSales,
    decimal CreditSales,
    int TotalExpenses,
    decimal TotalExpensesAmount,
    decimal CashExpenses,
    decimal NetCashInHand,
    IReadOnlyList<DayBookTransactionDto> Transactions);

[RequirePermission("reports.gst")]
public sealed record GetSalesSummaryQuery(DateTimeOffset FromUtc, DateTimeOffset ToUtc) : IQuery<SalesSummaryDto>;

[RequirePermission("reports.gst")]
public sealed record GetGstr1ReportQuery(DateTimeOffset FromUtc, DateTimeOffset ToUtc) : IQuery<Gstr1ReportDto>;

[RequirePermission("reports.gst")]
public sealed record GetGstr3bSummaryQuery(DateTimeOffset FromUtc, DateTimeOffset ToUtc) : IQuery<Gstr3bReportDto>;

[RequirePermission("reports.gst")]
public sealed record GetDayBookQuery(DateOnly Date) : IQuery<DayBookReportDto>;

[RequirePermission("reports.gst")]
public sealed record ExportTallyXmlQuery(DateTimeOffset FromUtc, DateTimeOffset ToUtc) : IQuery<string>;

internal sealed class ReportHandlers :
    IRequestHandler<GetSalesSummaryQuery, SalesSummaryDto>,
    IRequestHandler<GetGstr1ReportQuery, Gstr1ReportDto>,
    IRequestHandler<GetGstr3bSummaryQuery, Gstr3bReportDto>,
    IRequestHandler<GetDayBookQuery, DayBookReportDto>,
    IRequestHandler<ExportTallyXmlQuery, string>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public ReportHandlers(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<SalesSummaryDto> Handle(GetSalesSummaryQuery request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var invoices = await _db.Invoices.AsNoTracking()
            .Where(i => i.BusinessId == businessId
                        && i.InvoiceDate >= request.FromUtc
                        && i.InvoiceDate <= request.ToUtc
                        && (i.InvoiceType == InvoiceType.TaxInvoice || i.InvoiceType == InvoiceType.BillOfSupply)
                        && (i.Status == InvoiceStatus.Confirmed || i.Status == InvoiceStatus.Partial || i.Status == InvoiceStatus.Paid))
            .ToListAsync(ct);

        return new SalesSummaryDto(
            InvoiceCount: invoices.Count,
            GrossSales: invoices.Sum(i => i.GrandTotal),
            TaxableTotal: invoices.Sum(i => i.TaxableAmount),
            CgstTotal: invoices.Sum(i => i.CgstTotal),
            SgstTotal: invoices.Sum(i => i.SgstTotal),
            IgstTotal: invoices.Sum(i => i.IgstTotal),
            CessTotal: invoices.Sum(i => i.CessTotal),
            CollectedTotal: invoices.Sum(i => i.AmountPaid),
            OutstandingCreditTotal: invoices.Sum(i => i.GrandTotal - i.AmountPaid));
    }

    public async Task<Gstr1ReportDto> Handle(GetGstr1ReportQuery request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var invoices = await _db.Invoices.AsNoTracking()
            .Include(i => i.Items)
            .Where(i => i.BusinessId == businessId
                        && i.InvoiceDate >= request.FromUtc
                        && i.InvoiceDate <= request.ToUtc
                        && i.InvoiceType == InvoiceType.TaxInvoice
                        && (i.Status == InvoiceStatus.Confirmed || i.Status == InvoiceStatus.Partial || i.Status == InvoiceStatus.Paid))
            .ToListAsync(ct);

        var customerIds = invoices.Where(i => i.CustomerId.HasValue).Select(i => i.CustomerId!.Value).Distinct().ToArray();
        var customers = await _db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var b2b = new List<Gstr1B2BEntry>();
        decimal b2cTaxable = 0m, b2cTax = 0m;

        foreach (var inv in invoices)
        {
            if (inv.CustomerId.HasValue && customers.TryGetValue(inv.CustomerId.Value, out var cust) && !string.IsNullOrWhiteSpace(cust.Gstin))
            {
                b2b.Add(new Gstr1B2BEntry(
                    cust.Gstin, cust.Name, inv.InvoiceNumber, inv.InvoiceDate, inv.PlaceOfSupplyCode,
                    inv.TaxableAmount, inv.CgstTotal, inv.SgstTotal, inv.IgstTotal, inv.CessTotal, inv.GrandTotal));
            }
            else
            {
                b2cTaxable += inv.TaxableAmount;
                b2cTax += inv.CgstTotal + inv.SgstTotal + inv.IgstTotal + inv.CessTotal;
            }
        }

        var hsnSummary = invoices
            .SelectMany(i => i.Items)
            .GroupBy(it => new { Hsn = it.HsnSacCode ?? "UNSPECIFIED", it.Unit })
            .Select(g => new Gstr1HsnEntry(
                g.Key.Hsn,
                g.Key.Unit,
                g.Sum(x => x.Quantity),
                g.Sum(x => x.TaxableAmount),
                g.Sum(x => x.CgstAmount + x.SgstAmount + x.IgstAmount + x.CessAmount)))
            .OrderBy(h => h.HsnSacCode)
            .ToArray();

        return new Gstr1ReportDto(b2b, b2cTaxable, b2cTax, hsnSummary);
    }

    public async Task<Gstr3bReportDto> Handle(GetGstr3bSummaryQuery request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var invoices = await _db.Invoices.AsNoTracking()
            .Include(i => i.Items)
            .Where(i => i.BusinessId == businessId
                        && i.InvoiceDate >= request.FromUtc
                        && i.InvoiceDate <= request.ToUtc
                        && (i.InvoiceType == InvoiceType.TaxInvoice || i.InvoiceType == InvoiceType.BillOfSupply)
                        && (i.Status == InvoiceStatus.Confirmed || i.Status == InvoiceStatus.Partial || i.Status == InvoiceStatus.Paid))
            .ToListAsync(ct);

        var compositionTurnover = invoices
            .Where(i => i.IsCompositionScheme || i.InvoiceType == InvoiceType.BillOfSupply)
            .Sum(i => i.GrandTotal);

        var slabs = invoices
            .Where(i => !i.IsCompositionScheme && i.InvoiceType == InvoiceType.TaxInvoice)
            .SelectMany(i => i.Items)
            .GroupBy(it => it.IgstRate > 0 ? it.IgstRate : it.CgstRate + it.SgstRate)
            .Select(g => new Gstr3bSlabSummary(
                g.Key,
                g.Sum(x => x.TaxableAmount),
                g.Sum(x => x.CgstAmount),
                g.Sum(x => x.SgstAmount),
                g.Sum(x => x.IgstAmount),
                g.Sum(x => x.CessAmount)))
            .OrderBy(s => s.TaxRatePercent)
            .ToArray();

        return new Gstr3bReportDto(compositionTurnover, slabs);
    }

    public async Task<string> Handle(ExportTallyXmlQuery request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var invoices = await _db.Invoices.AsNoTracking()
            .Where(i => i.BusinessId == businessId
                        && i.InvoiceDate >= request.FromUtc
                        && i.InvoiceDate <= request.ToUtc
                        && (i.Status == InvoiceStatus.Confirmed || i.Status == InvoiceStatus.Partial || i.Status == InvoiceStatus.Paid))
            .OrderBy(i => i.InvoiceDate)
            .ToListAsync(ct);

        var envelope = new XElement("ENVELOPE",
            new XElement("HEADER", new XElement("TALLYREQUEST", "Import Data")),
            new XElement("BODY",
                new XElement("IMPORTDATA",
                    new XElement("REQUESTDESC", new XElement("REPORTNAME", "Vouchers")),
                    new XElement("REQUESTDATA",
                        invoices.Select(inv =>
                            new XElement("TALLYMESSAGE",
                                new XElement("VOUCHER",
                                    new XAttribute("VCHTYPE", "Sales"),
                                    new XAttribute("ACTION", "Create"),
                                    new XElement("DATE", inv.InvoiceDate.ToOffset(TimeSpan.FromHours(5.5)).ToString("yyyyMMdd")),
                                    new XElement("VOUCHERNUMBER", inv.InvoiceNumber),
                                    new XElement("PARTYLEDGERNAME", "Cash / Counter Sales"),
                                    new XElement("EFFECTIVEDATE", inv.InvoiceDate.ToOffset(TimeSpan.FromHours(5.5)).ToString("yyyyMMdd")),
                                    new XElement("AMOUNT", inv.GrandTotal.ToString("0.00")))))))));

        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), envelope).ToString();
    }

    public async Task<DayBookReportDto> Handle(GetDayBookQuery request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var istOffset = TimeSpan.FromHours(5.5);
        var startUtc = new DateTimeOffset(request.Date.Year, request.Date.Month, request.Date.Day, 0, 0, 0, istOffset).ToUniversalTime();
        var endUtc = startUtc.AddDays(1).AddTicks(-1);

        var invoices = await _db.Invoices.AsNoTracking()
            .Include(i => i.Payments)
            .Where(i => i.BusinessId == businessId
                        && i.InvoiceDate >= startUtc
                        && i.InvoiceDate <= endUtc
                        && (i.Status == InvoiceStatus.Confirmed || i.Status == InvoiceStatus.Partial || i.Status == InvoiceStatus.Paid))
            .OrderBy(i => i.InvoiceDate)
            .ToListAsync(ct);

        var expenses = await _db.Expenses.AsNoTracking()
            .Where(e => e.BusinessId == businessId && e.ExpenseDate == request.Date)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

        decimal cashSales = 0m, upiSales = 0m, cardSales = 0m, creditSales = 0m;
        var transactions = new List<DayBookTransactionDto>();

        foreach (var inv in invoices)
        {
            var paidByCash = inv.Payments.Where(p => p.PaymentMode == PaymentMode.Cash).Sum(p => p.Amount);
            var paidByUpi = inv.Payments.Where(p => p.PaymentMode == PaymentMode.Upi).Sum(p => p.Amount);
            var paidByCard = inv.Payments.Where(p => p.PaymentMode == PaymentMode.Card).Sum(p => p.Amount);
            var unpaidCredit = Math.Max(0m, inv.GrandTotal - inv.AmountPaid);

            cashSales += paidByCash;
            upiSales += paidByUpi;
            cardSales += paidByCard;
            creditSales += unpaidCredit;

            var primaryMode = inv.Payments.FirstOrDefault()?.PaymentMode ?? PaymentMode.Credit;
            transactions.Add(new DayBookTransactionDto(
                Time: inv.InvoiceDate.ToOffset(istOffset).ToString("hh:mm tt"),
                Type: "Sale Invoice",
                ReferenceNumber: inv.InvoiceNumber,
                Description: $"Tax Invoice ({inv.Items.Count} items)",
                PaymentMode: primaryMode,
                Inflow: inv.GrandTotal,
                Outflow: 0m));
        }

        decimal cashExpenses = 0m, totalExpensesAmount = 0m;
        foreach (var exp in expenses)
        {
            totalExpensesAmount += exp.Amount;
            if (exp.PaymentMode == PaymentMode.Cash)
                cashExpenses += exp.Amount;

            transactions.Add(new DayBookTransactionDto(
                Time: exp.CreatedAt.ToOffset(istOffset).ToString("hh:mm tt"),
                Type: "Store Expense",
                ReferenceNumber: "EXP",
                Description: string.IsNullOrWhiteSpace(exp.Description) ? "Expense" : exp.Description,
                PaymentMode: exp.PaymentMode,
                Inflow: 0m,
                Outflow: exp.Amount));
        }

        return new DayBookReportDto(
            Date: request.Date,
            TotalInvoices: invoices.Count,
            TotalSales: invoices.Sum(i => i.GrandTotal),
            CashSales: cashSales,
            UpiSales: upiSales,
            CardSales: cardSales,
            CreditSales: creditSales,
            TotalExpenses: expenses.Count,
            TotalExpensesAmount: totalExpensesAmount,
            CashExpenses: cashExpenses,
            NetCashInHand: cashSales - cashExpenses,
            Transactions: transactions);
    }
}
