using BillMate.Domain.Enums;

namespace BillMate.Application.Common.Interfaces;

public enum InvoicePageFormat
{
    A4,
    A5,
    Thermal80mm
}

public sealed record InvoicePdfLineModel(
    string ItemName,
    string? HsnSacCode,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TaxableAmount,
    decimal CgstRate,
    decimal CgstAmount,
    decimal SgstRate,
    decimal SgstAmount,
    decimal IgstRate,
    decimal IgstAmount,
    decimal CessAmount,
    decimal LineTotal);

public sealed record InvoicePdfModel(
    string BusinessName,
    string? BusinessGstin,
    string BusinessAddress,
    string BusinessPhone,
    string BusinessStateCode,
    string? LogoPath,
    string? SignaturePath,
    string InvoiceNumber,
    InvoiceType InvoiceType,
    DateTimeOffset InvoiceDate,
    bool IsInterState,
    bool IsCompositionScheme,
    bool IsReverseCharge,
    string PlaceOfSupplyCode,
    string? CustomerName,
    string? CustomerGstin,
    string? CustomerPhone,
    string? CustomerAddress,
    string? EwayBillNumber,
    string? VehicleNumber,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxableAmount,
    decimal CgstTotal,
    decimal SgstTotal,
    decimal IgstTotal,
    decimal CessTotal,
    decimal RoundOff,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal AmountDue,
    string AmountInWords,
    string? UpiVpa,
    string? Notes,
    IReadOnlyList<InvoicePdfLineModel> Items);

public interface IInvoicePdfGenerator
{
    byte[] GenerateInvoicePdf(InvoicePdfModel model, InvoicePageFormat format = InvoicePageFormat.A4);
}

public interface IEscPosReceiptBuilder
{
    byte[] BuildReceipt(InvoicePdfModel model, int paperWidthMm = 80, bool openCashDrawer = false);
    byte[] BuildCashDrawerKickCommand();
}

public sealed record DriveBackupFileDto(string FileId, string FileName, long SizeBytes, DateTimeOffset CreatedAtUtc);

public interface IBackupStorageClient
{
    string BuildAuthorizationUrl(string state, string codeChallenge, string redirectUri);
    Task<string> ExchangeAuthorizationCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken ct = default);
    Task<string> UploadResumableAsync(string refreshToken, string businessFolderName, string localFilePath, string fileName, CancellationToken ct = default);
    Task<IReadOnlyList<DriveBackupFileDto>> ListBackupsAsync(string refreshToken, string businessFolderName, CancellationToken ct = default);
    Task DownloadBackupAsync(string refreshToken, string remoteFileId, string destinationPath, CancellationToken ct = default);
    Task<int> PruneOldBackupsAsync(string refreshToken, string businessFolderName, int keepDays = 30, int minKeepCount = 5, CancellationToken ct = default);
}

public interface IPgToolsRunner
{
    Task DumpCustomFormatAsync(string connectionString, string outputDumpPath, CancellationToken ct = default);
    Task VerifyDumpAsync(string dumpPath, CancellationToken ct = default);
    Task RecreateDatabaseAsync(string connectionString, CancellationToken ct = default);
    Task RestoreDumpAsync(string connectionString, string dumpPath, CancellationToken ct = default);
}

public interface IBackupOrchestrator
{
    Task<Guid> RunBackupAsync(Guid businessId, BackupTarget target, CancellationToken ct = default);
    Task RestoreFromDriveAsync(Guid businessId, string remoteFileId, CancellationToken ct = default);
}
