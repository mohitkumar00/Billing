using BillMate.Application.Common.Interfaces;
using BillMate.Application.Features.Auth;
using BillMate.Application.Features.Backup;
using BillMate.Application.Features.Invoices;
using BillMate.Application.Features.Parties;
using BillMate.Application.Features.Products;
using BillMate.Application.Features.Reports;
using BillMate.Application.Features.Sync;
using Refit;

namespace BillMate.ClientCore.Api;

public interface IBillMateApi
{
    [Post("/api/v1/auth/login")]
    Task<AuthResponse> LoginAsync([Body] LoginCommand command, CancellationToken ct = default);

    [Post("/api/v1/auth/login/pin")]
    Task<AuthResponse> PinLoginAsync([Body] PinLoginCommand command, CancellationToken ct = default);

    [Post("/api/v1/auth/refresh")]
    Task<AuthResponse> RefreshTokenAsync([Body] RefreshTokenCommand command, CancellationToken ct = default);

    [Get("/api/v1/products/search")]
    Task<IReadOnlyList<ProductDto>> SearchProductsAsync([Query] string? q = null, [Query] int limit = 50, CancellationToken ct = default);

    [Get("/api/v1/products/low-stock")]
    Task<IReadOnlyList<ProductDto>> GetLowStockProductsAsync(CancellationToken ct = default);

    [Post("/api/v1/invoices")]
    Task<InvoiceDto> CreateInvoiceAsync([Body] CreateInvoiceCommand command, CancellationToken ct = default);

    [Get("/api/v1/invoices")]
    Task<IReadOnlyList<InvoiceSummaryDto>> GetInvoicesAsync([Query] int page = 1, [Query] int pageSize = 50, CancellationToken ct = default);

    [Get("/api/v1/invoices/{id}/escpos")]
    Task<HttpResponseMessage> GetEscPosReceiptAsync(Guid id, [Query] int width = 80, [Query] bool openDrawer = false, CancellationToken ct = default);

    [Get("/api/v1/customers")]
    Task<IReadOnlyList<CustomerDto>> SearchCustomersAsync([Query] string? q = null, [Query] int limit = 50, CancellationToken ct = default);

    [Get("/api/v1/reports/sales-summary")]
    Task<SalesSummaryDto> GetSalesSummaryAsync([Query] DateTimeOffset from, [Query] DateTimeOffset to, CancellationToken ct = default);

    [Get("/api/v1/backup/drive/list")]
    Task<IReadOnlyList<DriveBackupFileDto>> ListDriveBackupsAsync(CancellationToken ct = default);

    [Post("/api/v1/backup/restore")]
    Task RestoreFromDriveAsync([Body] RestoreFromDriveCommand command, CancellationToken ct = default);

    [Post("/api/v1/sync/batch")]
    Task<SyncBatchResult> SyncBatchAsync([Body] ProcessOfflineSyncBatchCommand command, CancellationToken ct = default);
}
