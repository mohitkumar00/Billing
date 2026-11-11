using BillMate.Application.Common.Interfaces;
using BillMate.Application.Features.Auth;
using BillMate.Application.Features.Backup;
using BillMate.Application.Features.Invoices;
using BillMate.Application.Features.Products;
using BillMate.Application.Features.Reports;
using BillMate.Application.Features.Sync;
using BillMate.ClientCore.Api;
using BillMate.ClientCore.Offline;
using BillMate.ClientCore.Services;
using BillMate.ClientCore.Theme;
using BillMate.ClientCore.ViewModels;
using BillMate.Domain.Enums;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BillMate.ClientCore.Tests;

public sealed class ClientCoreViewModelTests : IAsyncLifetime
{
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), $"billmate_client_test_{Guid.NewGuid():N}.db");
    private OfflineLocalStore _localStore = null!;

    public async Task InitializeAsync()
    {
        _localStore = new OfflineLocalStore(_sqlitePath);
        await _localStore.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _localStore.DisposeAsync();
        if (File.Exists(_sqlitePath)) File.Delete(_sqlitePath);
    }

    [Fact]
    public async Task PosViewModel_FallsBackToOfflineQueue_AndSyncsOnReconnect()
    {
        var api = Substitute.For<IBillMateApi>();
        var session = new ClientSessionState();
        var product = new ProductDto(Guid.NewGuid(), "SKU-1", "890123", "Masala Chai 250g", "PCS", "0902", Guid.NewGuid(), 5m, 100m, 110m, 20m, 5m, false, false, true, 1);

        // 1. Online search caches product locally
        api.SearchProductsAsync(Arg.Any<string?>(), 60, Arg.Any<CancellationToken>())
            .Returns([product]);
        var vm = new PosViewModel(api, _localStore, session);
        await vm.SearchCatalogCommand.ExecuteAsync(null);
        Assert.Single(vm.CatalogProducts);
        Assert.False(vm.IsOfflineMode);

        // 2. Add 2 units & Hold/Resume
        vm.AddToCart(product);
        vm.AddToCart(product);
        Assert.Equal(210m, vm.GrandTotal); // 200 + 5% GST = 210
        vm.HoldCurrentBill("Table 1");
        Assert.Empty(vm.CartLines);
        Assert.Single(vm.HeldBills);
        vm.ResumeHeldBill(vm.HeldBills[0]);
        Assert.Single(vm.CartLines);

        // 3. Server goes offline during checkout -> queues in SQLite WAL
        api.CreateInvoiceAsync(Arg.Any<CreateInvoiceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("LAN unreachable"));

        await vm.CheckoutCommand.ExecuteAsync(null);
        Assert.True(vm.IsOfflineMode);
        Assert.StartsWith("OFF-", vm.LastReceiptNumber);
        Assert.Empty(vm.CartLines);

        var queued = await _localStore.GetPendingOperationsAsync();
        Assert.Single(queued);

        // 4. Reconnect & flush sync queue
        api.SyncBatchAsync(Arg.Any<ProcessOfflineSyncBatchCommand>(), Arg.Any<CancellationToken>())
            .Returns(new SyncBatchResult(1, 0, 0));

        var coordinator = new OfflineSyncCoordinator(_localStore, api, session);
        var result = await coordinator.FlushPendingAsync();
        Assert.Equal(1, result.ProcessedCount);
        Assert.Empty(await _localStore.GetPendingOperationsAsync());
    }

    [Fact]
    public async Task DashboardViewModel_HandlesStatesAndCommandPaletteFilter()
    {
        var api = Substitute.For<IBillMateApi>();
        var session = new ClientSessionState();
        var coordinator = new OfflineSyncCoordinator(_localStore, api, session);

        api.GetSalesSummaryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SalesSummaryDto(12, 18450m, 16000m, 1225m, 1225m, 0m, 0m, 17000m, 1450m));
        api.GetLowStockProductsAsync(Arg.Any<CancellationToken>())
            .Returns([]);
        api.GetInvoicesAsync(1, 10, Arg.Any<CancellationToken>())
            .Returns([]);

        var vm = new DashboardViewModel(api, _localStore, coordinator);
        await vm.LoadDashboardCommand.ExecuteAsync(null);

        Assert.Equal(18450m, vm.TodaySalesTotal);
        Assert.True(vm.IsEmpty); // No recent invoices -> triggers Empty State UI
        Assert.False(vm.HasError);

        vm.CommandSearchQuery = "Restore";
        Assert.Single(vm.FilteredCommands);
        Assert.Equal("restore", vm.FilteredCommands[0].RouteKey);
    }

    [Fact]
    public async Task RestoreWizardAndPinLogin_TransitionCleanly()
    {
        var api = Substitute.For<IBillMateApi>();
        var backup = new DriveBackupFileDto("file-1", "billmate_20261111.dump.gz", 1048576, DateTimeOffset.UtcNow);
        api.ListDriveBackupsAsync(Arg.Any<CancellationToken>()).Returns([backup]);

        var wizard = new RestoreWizardViewModel(api);
        await wizard.LoadDriveBackupsCommand.ExecuteAsync(null);
        Assert.Equal(1, wizard.CurrentStep);
        Assert.Single(wizard.AvailableBackups);

        wizard.ProceedToConfirmation(backup);
        Assert.Equal(2, wizard.CurrentStep);

        await wizard.ExecuteRestoreCommand.ExecuteAsync(null);
        Assert.Equal(3, wizard.CurrentStep);
        Assert.True(wizard.IsRestoreCompleted);

        var session = new ClientSessionState();
        var userId = Guid.NewGuid();
        api.PinLoginAsync(Arg.Any<PinLoginCommand>(), Arg.Any<CancellationToken>())
            .Returns(new AuthResponse(userId, Guid.NewGuid(), "Ravi", "ravi@shop.in", "CASHIER", ["invoices.create"], "jwt", "ref"));

        var pinVm = new PinLoginViewModel(api, session) { SelectedUserId = userId };
        pinVm.AppendDigit("1");
        pinVm.AppendDigit("2");
        pinVm.AppendDigit("3");
        pinVm.AppendDigit("4");
        Assert.Equal("●●●●", pinVm.MaskedPinDots);
        await pinVm.SubmitPinCommand.ExecuteAsync(null);
        Assert.Equal("Ravi", session.CurrentAuth!.FullName);
    }

    [Fact]
    public void WhiteLabelConfig_ParsesJsonTemplate()
    {
        var cfg = WhiteLabelConfig.LoadFromJson("""{"businessName":"Gupta Mart","primaryColor":"#0D47A1","stateCode":"07"}""");
        Assert.Equal("Gupta Mart", cfg.BusinessName);
        Assert.Equal("#0D47A1", cfg.PrimaryColor);
        Assert.Equal("07", cfg.StateCode);
    }
}
