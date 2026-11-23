using BillMate.Application.Common.Interfaces;
using BillMate.Application.Features.Auth;
using BillMate.Application.Features.Backup;
using BillMate.Application.Features.Invoices;
using BillMate.Application.Features.Parties;
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
using System.Net;
using System.Net.Http.Headers;

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

        for (var attempt = 0; attempt < 20 && vm.CatalogProducts.Count == 0; attempt++)
        {
            await Task.Delay(25);
        }

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
    public async Task PosViewModel_Checkout_SavesPdf_AndAttemptsPrint()
    {
        var api = Substitute.For<IBillMateApi>();
        var session = new ClientSessionState();
        var businessId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var product = new ProductDto(Guid.NewGuid(), "SKU-7", "8900007", "Ground Coffee 200g", "PCS", "0901", Guid.NewGuid(), 20m, 200m, 220m, 10m, 5m, false, false, true, 1);

        api.CreateInvoiceAsync(Arg.Any<CreateInvoiceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new InvoiceDto(
                invoiceId,
                "INV-4201",
                "2026-27",
                InvoiceType.TaxInvoice,
                InvoiceStatus.Paid,
                DateTimeOffset.UtcNow,
                null,
                "27",
                false,
                false,
                220m,
                0m,
                209.52m,
                5.24m,
                5.24m,
                0m,
                0m,
                0m,
                220m,
                220m,
                0m,
                "Two Hundred Twenty",
                [new InvoiceItemDto(Guid.NewGuid(), "Ground Coffee 200g", "0901", "PCS", 1m, 220m, 0m, 209.52m, 2.5m, 5.24m, 2.5m, 5.24m, 0m, 0m, 0m, 220m)]));

        var pdfResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0x25, 0x50, 0x44, 0x46])
        };
        pdfResponse.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileName = "INV-4201.pdf"
        };

        api.GetInvoicePdfAsync(invoiceId, InvoicePageFormat.A4, Arg.Any<CancellationToken>())
            .Returns(pdfResponse);

        var vm = new PosViewModel(api, _localStore, session);
        vm.AddToCart(product);

        await vm.CheckoutCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(vm.LastSavedPdfPath));
        Assert.True(File.Exists(vm.LastSavedPdfPath));
        await api.Received(1).GetInvoicePdfAsync(invoiceId, InvoicePageFormat.A4, Arg.Any<CancellationToken>());

        if (!string.IsNullOrWhiteSpace(vm.LastSavedPdfPath) && File.Exists(vm.LastSavedPdfPath))
        {
            File.Delete(vm.LastSavedPdfPath);
        }
    }

    [Fact]
    public void PosViewModel_ClearCurrentBill_ResetsCartAndCustomerFields()
    {
        var api = Substitute.For<IBillMateApi>();
        var session = new ClientSessionState();
        var product = new ProductDto(Guid.NewGuid(), "SKU-11", "8900011", "Energy Drink", "PCS", "2202", Guid.NewGuid(), 30m, 90m, 100m, 24m, 12m, false, false, true, 1);

        var vm = new PosViewModel(api, _localStore, session);
        vm.CustomerPhone = "9999999999";
        vm.CustomerName = "Test Customer";
        vm.CustomerGstin = "27ABCDE1234F1Z5";
        vm.BillDiscountAmount = 10m;
        vm.AddToCart(product);

        vm.ClearCurrentBillCommand.Execute(null);

        Assert.Empty(vm.CartLines);
        Assert.Equal(string.Empty, vm.CustomerPhone);
        Assert.Equal(string.Empty, vm.CustomerName);
        Assert.Equal(string.Empty, vm.CustomerGstin);
        Assert.Equal(0m, vm.BillDiscountAmount);
    }

    [Fact]
    public async Task PosViewModel_LoadCustomerSuggestions_AndSelectCustomer_PopulatesFields()
    {
        var api = Substitute.For<IBillMateApi>();
        var session = new ClientSessionState();
        var vm = new PosViewModel(api, _localStore, session);

        var customer = new CustomerDto(Guid.NewGuid(), "Riya Stores", "9876543210", "27ABCDE1234F1Z5", "27", PricingTier.Retail, 0m, 0m, 0);
        api.SearchCustomersAsync("987", 8, Arg.Any<CancellationToken>()).Returns([customer]);

        await vm.LoadCustomerSuggestionsCommand.ExecuteAsync("987");

        Assert.True(vm.IsCustomerSuggestionsOpen);
        Assert.Single(vm.CustomerSuggestions);

        vm.SelectCustomerSuggestionCommand.Execute(customer);

        Assert.Equal(customer.Id, vm.SelectedCustomerId);
        Assert.Equal("9876543210", vm.CustomerPhone);
        Assert.Equal("Riya Stores", vm.CustomerName);
        Assert.Equal("27ABCDE1234F1Z5", vm.CustomerGstin);
        Assert.False(vm.IsCustomerSuggestionsOpen);
    }

    [Fact]
    public async Task PosViewModel_Checkout_WithSplitPayment_SendsMultiplePayments()
    {
        var api = Substitute.For<IBillMateApi>();
        var session = new ClientSessionState();
        var invoiceId = Guid.NewGuid();

        var product = new ProductDto(Guid.NewGuid(), "SKU-21", "8900021", "Premium Tea", "PCS", "0902", Guid.NewGuid(), 10m, 100m, 110m, 10m, 5m, false, false, true, 1);

        api.CreateInvoiceAsync(Arg.Any<CreateInvoiceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new InvoiceDto(
                invoiceId,
                "INV-5001",
                "2026-27",
                InvoiceType.TaxInvoice,
                InvoiceStatus.Paid,
                DateTimeOffset.UtcNow,
                null,
                "27",
                false,
                false,
                110m,
                0m,
                104.76m,
                2.62m,
                2.62m,
                0m,
                0m,
                0m,
                110m,
                110m,
                0m,
                "One Hundred Ten",
                [new InvoiceItemDto(Guid.NewGuid(), "Premium Tea", "0902", "PCS", 1m, 110m, 0m, 104.76m, 2.5m, 2.62m, 2.5m, 2.62m, 0m, 0m, 0m, 110m)]));

        var pdfResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0x25, 0x50, 0x44, 0x46])
        };
        pdfResponse.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileName = "INV-5001.pdf"
        };
        api.GetInvoicePdfAsync(invoiceId, InvoicePageFormat.A4, Arg.Any<CancellationToken>()).Returns(pdfResponse);

        var vm = new PosViewModel(api, _localStore, session);
        vm.AddToCart(product);
        vm.ToggleSplitPaymentCommand.Execute(null);
        vm.SplitCashAmount = 50m;
        vm.SplitUpiAmount = 60m;
        vm.BillNotes = "Delivered to office";

        await vm.CheckoutCommand.ExecuteAsync(null);

        await api.Received(1).CreateInvoiceAsync(
            Arg.Is<CreateInvoiceCommand>(c =>
                c.Payments != null &&
                c.Payments.Count == 2 &&
                c.Payments.Any(p => p.PaymentMode == PaymentMode.Cash && p.Amount == 50m) &&
                c.Payments.Any(p => p.PaymentMode == PaymentMode.Upi && p.Amount == 60m) &&
                c.Notes != null && c.Notes.Contains("Delivered to office")),
            Arg.Any<CancellationToken>());

        if (!string.IsNullOrWhiteSpace(vm.LastSavedPdfPath) && File.Exists(vm.LastSavedPdfPath))
        {
            File.Delete(vm.LastSavedPdfPath);
        }
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

        var vm = new DashboardViewModel(api, _localStore, coordinator, session);
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

    [Fact]
    public void DashboardViewModel_HidesCancelForUnauthorizedRole()
    {
        var api = Substitute.For<IBillMateApi>();
        var session = new ClientSessionState
        {
            CurrentAuth = new AuthResponse(Guid.NewGuid(), Guid.NewGuid(), "Cashier", "cashier@billmate.local", "CASHIER", ["invoices.create"], "jwt", "rt")
        };

        var coordinator = new OfflineSyncCoordinator(_localStore, api, session);
        var vm = new DashboardViewModel(api, _localStore, coordinator, session);

        Assert.False(vm.CanCancelInvoices);
    }

    [Fact]
    public async Task InvoicesViewModel_CancelsSelectedInvoice_WhenAuthorized()
    {
        var api = Substitute.For<IBillMateApi>();
        var businessId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();

        var session = new ClientSessionState
        {
            CurrentAuth = new AuthResponse(Guid.NewGuid(), businessId, "Admin", "admin@billmate.local", "ADMIN", ["invoices.create", "invoices.cancel"], "jwt", "rt")
        };

        var summary = new InvoiceSummaryDto(invoiceId, "INV-1001", InvoiceType.TaxInvoice, InvoiceStatus.Paid, DateTimeOffset.UtcNow, null, 250m, 250m, 0m);
        var detail = new InvoiceDto(
            invoiceId,
            "INV-1001",
            "2026-27",
            InvoiceType.TaxInvoice,
            InvoiceStatus.Paid,
            DateTimeOffset.UtcNow,
            null,
            "27",
            false,
            false,
            250m,
            0m,
            211.86m,
            19.07m,
            19.07m,
            0m,
            0m,
            0m,
            250m,
            250m,
            0m,
            "Two Hundred Fifty",
            [new InvoiceItemDto(Guid.NewGuid(), "Demo Item", "8517", "PCS", 1m, 250m, 0m, 211.86m, 9m, 19.07m, 9m, 19.07m, 0m, 0m, 0m, 250m)]);

        api.QueryInvoicesAsync(Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<InvoiceStatus?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([summary]);
        api.GetInvoiceByIdAsync(invoiceId, Arg.Any<CancellationToken>()).Returns(detail);
        api.CancelInvoiceAsync(invoiceId, Arg.Any<CancelInvoiceBody>(), Arg.Any<CancellationToken>())
            .Returns(new CreditNoteDto(Guid.NewGuid(), invoiceId, "CN-0001", "Wrong bill", 250m, DateTimeOffset.UtcNow));

        var vm = new InvoicesViewModel(api, session);
        await vm.LoadInvoicesCommand.ExecuteAsync(null);
        await vm.SelectInvoiceCommand.ExecuteAsync(summary);
        vm.CancelReason = "Wrong bill";

        await vm.CancelSelectedInvoiceCommand.ExecuteAsync(null);

        await api.Received(1).CancelInvoiceAsync(invoiceId, Arg.Is<CancelInvoiceBody>(b => b.Reason == "Wrong bill"), Arg.Any<CancellationToken>());
    }
}
