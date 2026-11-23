using BillMate.Domain.Enums;

namespace BillMate.ClientCore.Api;

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
