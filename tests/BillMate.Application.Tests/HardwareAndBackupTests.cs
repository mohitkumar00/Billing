using System.Text;
using BillMate.Application.Common.Interfaces;
using BillMate.Application.Features.Backup;
using BillMate.Domain.Enums;
using BillMate.Infrastructure.Backup;
using BillMate.Infrastructure.Persistence;
using BillMate.Infrastructure.Persistence.Interceptors;
using BillMate.Infrastructure.Persistence.Seed;
using BillMate.Infrastructure.Printing;
using BillMate.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BillMate.Application.Tests;

public class HardwareAndBackupTests
{
    private static InvoicePdfModel SampleModel(bool isComposition = false) => new(
        BusinessName: "Sharma General Store",
        BusinessGstin: isComposition ? null : "27AAACP1234A1Z5",
        BusinessAddress: "12 MG Road, Mumbai, Maharashtra - 400001",
        BusinessPhone: "9876543210",
        BusinessStateCode: "27",
        LogoPath: null,
        SignaturePath: null,
        InvoiceNumber: "INV-2627-0001",
        InvoiceType: isComposition ? InvoiceType.BillOfSupply : InvoiceType.TaxInvoice,
        InvoiceDate: DateTimeOffset.UtcNow,
        IsInterState: false,
        IsCompositionScheme: isComposition,
        IsReverseCharge: false,
        PlaceOfSupplyCode: "27",
        CustomerName: "Ramesh Kumar",
        CustomerGstin: null,
        CustomerPhone: "9811122233",
        CustomerAddress: null,
        EwayBillNumber: null,
        VehicleNumber: null,
        Subtotal: 200m,
        DiscountTotal: 0m,
        TaxableAmount: 200m,
        CgstTotal: isComposition ? 0m : 18m,
        SgstTotal: isComposition ? 0m : 18m,
        IgstTotal: 0m,
        CessTotal: 0m,
        RoundOff: 0m,
        GrandTotal: isComposition ? 200m : 236m,
        AmountPaid: isComposition ? 200m : 236m,
        AmountDue: 0m,
        AmountInWords: "Rupees Two Hundred Thirty Six Only",
        UpiVpa: "sharmastore@upi",
        Notes: "Thank you!",
        Items:
        [
            new InvoicePdfLineModel("Cooking Oil 1L", "1507", "LTR", 1m, 200m, 0m, 200m, 9m, 18m, 9m, 18m, 0m, 0m, 0m, 236m)
        ]);

    [Theory]
    [InlineData(InvoicePageFormat.A4, false)]
    [InlineData(InvoicePageFormat.A5, false)]
    [InlineData(InvoicePageFormat.Thermal80mm, false)]
    [InlineData(InvoicePageFormat.A4, true)]
    public void QuestPdf_GeneratesValidPdfDocument(InvoicePageFormat format, bool isComposition)
    {
        var generator = new QuestPdfInvoiceGenerator();
        var pdf = generator.GenerateInvoicePdf(SampleModel(isComposition), format);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }

    [Fact]
    public void EscPosBuilder_GeneratesThermalBytes_AndIncludesCashDrawerKick()
    {
        var builder = new EscPosReceiptBuilder();
        var withoutDrawer = builder.BuildReceipt(SampleModel(), paperWidthMm: 58, openCashDrawer: false);
        var withDrawer = builder.BuildReceipt(SampleModel(), paperWidthMm: 80, openCashDrawer: true);

        var text58 = Encoding.ASCII.GetString(withoutDrawer);
        Assert.Contains("Sharma General Store", text58);
        Assert.Contains("INV-2627-0001", text58);

        var kick = builder.BuildCashDrawerKickCommand();
        Assert.Equal(new byte[] { 0x1B, 0x70, 0x00, 0x19, 0xFA }, kick);
        Assert.Equal(kick, withDrawer.TakeLast(kick.Length).ToArray());
        Assert.NotEqual(kick, withoutDrawer.TakeLast(kick.Length).ToArray());
    }

    [Fact]
    public async Task BackupOrchestrator_DumpsVerifiesUploadsAndPrunes()
    {
        await using var conn = new SqliteConnection("Filename=:memory:");
        await conn.OpenAsync();

        var interceptor = new AuditableAndAuditLogInterceptor(new NullCurrentUser());
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).AddInterceptors(interceptor).Options;
        await using var db = new AppDbContext(opts);
        await db.Database.EnsureCreatedAsync();
        await DatabaseSeeder.SeedDefaultsAsync(db, new BcryptPasswordHasher());

        var business = await db.Businesses.FirstAsync();
        db.AppSettings.Add(Domain.Entities.AppSetting.Create(
            business.Id, BackupConstants.DriveTokenSettingKey, null, Encoding.UTF8.GetBytes("refresh-tok-123"), isSecret: true));
        await db.SaveChangesAsync();

        var drive = Substitute.For<IBackupStorageClient>();
        drive.UploadResumableAsync("refresh-tok-123", business.Name, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("drive-file-id-99");

        var pgTools = Substitute.For<IPgToolsRunner>();
        pgTools.DumpCustomFormatAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => File.WriteAllTextAsync(call.ArgAt<string>(1), "PGDMP-TEST-DATA"));

        var config = Substitute.For<IConfiguration>();
        var connSection = Substitute.For<IConfigurationSection>();
        connSection["DefaultConnection"].Returns("Host=localhost;Database=billmatedb");
        config.GetSection("ConnectionStrings").Returns(connSection);

        var orchestrator = new BackupOrchestrator(db, drive, pgTools, config, NullLogger<BackupOrchestrator>.Instance);
        var logId = await orchestrator.RunBackupAsync(business.Id, BackupTarget.GoogleDrive);

        var log = await db.BackupLogs.FirstAsync(l => l.Id == logId);
        Assert.Equal(BackupStatus.Uploaded, log.Status);
        Assert.Equal("drive-file-id-99", log.RemoteFileId);
        Assert.True(log.FileSizeBytes > 0);
        await pgTools.Received(1).VerifyDumpAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await drive.Received(1).PruneOldBackupsAsync("refresh-tok-123", business.Name, 30, 5, Arg.Any<CancellationToken>());
    }

    private sealed class NullCurrentUser : ICurrentUserService
    {
        public Guid? UserId => null;
        public Guid? BusinessId => null;
        public Guid? DeviceId => null;
        public string? IpAddress => null;
        public IReadOnlyCollection<string> Permissions => [];
    }
}
