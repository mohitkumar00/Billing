using BillMate.Application.Common.Interfaces;
using BillMate.Application.Features.Auth;
using BillMate.Application.Features.Backup;
using BillMate.Application.Features.Invoices;
using BillMate.Application.Features.Parties;
using BillMate.Application.Features.Products;
using BillMate.Application.Features.Reports;
using BillMate.Application.Features.Sync;
using BillMate.Domain.Enums;
using MediatR;

namespace BillMate.Api.Endpoints;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapBillMateApiV1(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v1");

        // 1. Auth (Rate limited at 10 req/min per IP)
        var auth = v1.MapGroup("/auth").RequireRateLimiting("AuthRateLimit");
        auth.MapPost("/login", async (LoginCommand cmd, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(cmd, ct)));
        auth.MapPost("/login/pin", async (PinLoginCommand cmd, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(cmd, ct)));
        auth.MapPost("/refresh", async (RefreshTokenCommand cmd, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(cmd, ct)));
        auth.MapPost("/logout", async (LogoutCommand cmd, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(cmd, ct);
            return Results.NoContent();
        });

        // 2. Protected API groups
        var secured = v1.MapGroup(string.Empty).RequireAuthorization();

        // Products & Inventory
        var products = secured.MapGroup("/products");
        products.MapGet("/search", async (string? q, int? limit, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new SearchProductsQuery(q, limit ?? 50), ct)));
        products.MapGet("/low-stock", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetLowStockProductsQuery(), ct)));
        products.MapPost("/", async (CreateProductCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var created = await sender.Send(cmd, ct);
            return Results.Created($"/api/v1/products/{created.Id}", created);
        });
        products.MapPost("/{id:guid}/stock-adjust", async (Guid id, StockAdjustBody body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new AdjustStockCommand(id, body.QuantityDelta, body.Reason), ct)));

        // Invoices & Credit Notes
        var invoices = secured.MapGroup("/invoices");
        invoices.MapPost("/", async (CreateInvoiceCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var invoice = await sender.Send(cmd, ct);
            return Results.Created($"/api/v1/invoices/{invoice.Id}", invoice);
        });
        invoices.MapGet("/", async (DateTimeOffset? from, DateTimeOffset? to, InvoiceStatus? status, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetInvoicesQuery(from, to, status, page ?? 1, pageSize ?? 50), ct)));
        invoices.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetInvoiceByIdQuery(id), ct)));
        invoices.MapPost("/{id:guid}/cancel", async (Guid id, CancelInvoiceBody body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new CancelInvoiceCommand(id, body.Reason), ct)));
        invoices.MapGet("/{id:guid}/pdf", async (Guid id, InvoicePageFormat? format, ISender sender, CancellationToken ct) =>
        {
            var pdf = await sender.Send(new GetInvoicePdfQuery(id, format ?? InvoicePageFormat.A4), ct);
            return Results.File(pdf.Content, pdf.ContentType, pdf.FileName);
        });
        invoices.MapGet("/{id:guid}/escpos", async (Guid id, int? width, bool? openDrawer, ISender sender, CancellationToken ct) =>
        {
            var bytes = await sender.Send(new GetThermalReceiptBytesQuery(id, width ?? 80, openDrawer ?? false), ct);
            return Results.File(bytes, "application/octet-stream", $"receipt_{id:N}.bin");
        });

        // Customers, Suppliers & Purchase Orders
        var customers = secured.MapGroup("/customers");
        customers.MapGet("/", async (string? q, int? limit, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new SearchCustomersQuery(q, limit ?? 50), ct)));
        customers.MapPost("/", async (CreateCustomerCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var created = await sender.Send(cmd, ct);
            return Results.Created($"/api/v1/customers/{created.Id}", created);
        });
        customers.MapPost("/{id:guid}/payments", async (Guid id, CustomerPaymentBody body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new RecordCustomerPaymentCommand(id, body.InvoiceId, body.PaymentMode, body.Amount, body.ReferenceNumber, body.Notes), ct)));

        secured.MapPost("/suppliers", async (CreateSupplierCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var created = await sender.Send(cmd, ct);
            return Results.Created($"/api/v1/suppliers/{created.Id}", created);
        });

        var po = secured.MapGroup("/purchase-orders");
        po.MapPost("/", async (CreatePurchaseOrderCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var id = await sender.Send(cmd, ct);
            return Results.Created($"/api/v1/purchase-orders/{id}", new { id });
        });
        po.MapPost("/{id:guid}/receive", async (Guid id, ReceivePoBody? body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ReceivePurchaseOrderCommand(id, body?.SupplierInvoiceNo), ct);
            return Results.NoContent();
        });

        // Reports & Tally XML
        var reports = secured.MapGroup("/reports");
        reports.MapGet("/sales-summary", async (DateTimeOffset from, DateTimeOffset to, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetSalesSummaryQuery(from, to), ct)));
        reports.MapGet("/gstr1", async (DateTimeOffset from, DateTimeOffset to, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetGstr1ReportQuery(from, to), ct)));
        reports.MapGet("/gstr3b", async (DateTimeOffset from, DateTimeOffset to, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetGstr3bSummaryQuery(from, to), ct)));
        reports.MapGet("/tally-export", async (DateTimeOffset from, DateTimeOffset to, ISender sender, CancellationToken ct) =>
        {
            var xml = await sender.Send(new ExportTallyXmlQuery(from, to), ct);
            return Results.Content(xml, "application/xml");
        });

        // Offline Sync Batch
        secured.MapPost("/sync/batch", async (ProcessOfflineSyncBatchCommand cmd, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(cmd, ct)));

        // Backup & Google Drive Restore
        var backup = secured.MapGroup("/backup");
        backup.MapPost("/run", async (RunBackupBody? body, ISender sender, CancellationToken ct) =>
            Results.Ok(new { backupLogId = await sender.Send(new RunBackupCommand(body?.Target ?? BackupTarget.GoogleDrive), ct) }));
        backup.MapGet("/history", async (int? limit, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetBackupHistoryQuery(limit ?? 30), ct)));
        backup.MapGet("/drive/auth-url", async (string redirectUri, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetDriveAuthUrlQuery(redirectUri), ct)));
        backup.MapPost("/drive/complete", async (CompleteDriveAuthCommand cmd, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(cmd, ct);
            return Results.NoContent();
        });
        backup.MapGet("/drive/list", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListDriveBackupsQuery(), ct)));
        backup.MapPost("/restore", async (RestoreFromDriveCommand cmd, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(cmd, ct);
            return Results.NoContent();
        });

        return app;
    }

    public sealed record StockAdjustBody(decimal QuantityDelta, string Reason);
    public sealed record CancelInvoiceBody(string Reason);
    public sealed record CustomerPaymentBody(Guid? InvoiceId, PaymentMode PaymentMode, decimal Amount, string? ReferenceNumber, string? Notes);
    public sealed record ReceivePoBody(string? SupplierInvoiceNo);
    public sealed record RunBackupBody(BackupTarget Target);
}
