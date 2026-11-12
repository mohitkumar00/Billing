using System.Collections.ObjectModel;
using BillMate.Application.Features.Invoices;
using BillMate.ClientCore.Api;
using BillMate.ClientCore.Offline;
using BillMate.ClientCore.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BillMate.ClientCore.ViewModels;

public sealed record QuickCommandItem(string Title, string Category, string ShortcutHint, string RouteKey);

public sealed partial class DashboardViewModel : StatefulViewModelBase
{
    private readonly IBillMateApi _api;
    private readonly OfflineLocalStore _localStore;
    private readonly OfflineSyncCoordinator _syncCoordinator;
    private readonly INavigationService? _navigation;

    private static readonly QuickCommandItem[] AllCommands =
    [
        new("New POS Bill", "Actions", "F2", "pos"),
        new("View Invoices", "Navigation", "F3", "invoices"),
        new("Low Stock Alerts", "Inventory", "F4", "inventory"),
        new("GSTR-1 & GSTR-3B Reports", "Compliance", "F5", "reports"),
        new("Google Drive Restore Wizard", "Backup", "F8", "restore")
    ];

    public ObservableCollection<InvoiceSummaryDto> RecentInvoices { get; } = [];
    public ObservableCollection<QuickCommandItem> FilteredCommands { get; } = [];

    [ObservableProperty]
    private decimal _todaySalesTotal;

    [ObservableProperty]
    private int _todayInvoiceCount;

    [ObservableProperty]
    private decimal _creditOutstandingTotal;

    [ObservableProperty]
    private int _lowStockCount;

    [ObservableProperty]
    private int _pendingSyncCount;

    [ObservableProperty]
    private bool _isCommandPaletteOpen;

    [ObservableProperty]
    private string _commandSearchQuery = string.Empty;

    public DashboardViewModel(
        IBillMateApi api,
        OfflineLocalStore localStore,
        OfflineSyncCoordinator syncCoordinator,
        INavigationService? navigation = null)
    {
        _api = api;
        _localStore = localStore;
        _syncCoordinator = syncCoordinator;
        _navigation = navigation;
        RefreshCommandFilter();
    }

    [RelayCommand]
    public async Task NavigateToPosAsync()
    {
        if (_navigation != null)
        {
            await _navigation.NavigateToAsync("//pos");
        }
    }

    [RelayCommand]
    public async Task NavigateToInventoryAsync()
    {
        if (_navigation != null)
        {
            await _navigation.NavigateToAsync("//inventory");
        }
    }

    [RelayCommand]
    public async Task NavigateToPinLoginAsync()
    {
        if (_navigation != null)
        {
            await _navigation.NavigateToAsync("//pinlogin");
        }
    }

    [RelayCommand]
    public async Task NavigateToRestoreAsync()
    {
        if (_navigation != null)
        {
            await _navigation.NavigateToAsync("//restore");
        }
    }

    [RelayCommand]
    public async Task NavigateToReportsAsync()
    {
        if (_navigation != null)
        {
            await _navigation.NavigateToAsync("//reports");
        }
    }

    [RelayCommand]
    public async Task ExecuteQuickCommandAsync(QuickCommandItem cmd)
    {
        if (_navigation != null && !string.IsNullOrWhiteSpace(cmd.RouteKey))
        {
            await _navigation.NavigateToAsync($"//{cmd.RouteKey}");
        }
    }

    partial void OnCommandSearchQueryChanged(string value) => RefreshCommandFilter();

    private void RefreshCommandFilter()
    {
        FilteredCommands.Clear();
        var matches = string.IsNullOrWhiteSpace(CommandSearchQuery)
            ? AllCommands
            : AllCommands.Where(c => c.Title.Contains(CommandSearchQuery.Trim(), StringComparison.OrdinalIgnoreCase)
                                  || c.Category.Contains(CommandSearchQuery.Trim(), StringComparison.OrdinalIgnoreCase));
        foreach (var item in matches) FilteredCommands.Add(item);
    }

    [RelayCommand]
    public async Task LoadDashboardAsync(CancellationToken ct = default)
    {
        await SafeExecuteAsync(async () =>
        {
            var istNow = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5));
            var startOfDay = new DateTimeOffset(istNow.Year, istNow.Month, istNow.Day, 0, 0, 0, istNow.Offset).ToUniversalTime();

            var summary = await _api.GetSalesSummaryAsync(startOfDay, DateTimeOffset.UtcNow, ct);
            TodaySalesTotal = summary.GrossSales;
            TodayInvoiceCount = summary.InvoiceCount;
            CreditOutstandingTotal = summary.OutstandingCreditTotal;

            var lowStock = await _api.GetLowStockProductsAsync(ct);
            LowStockCount = lowStock.Count;

            var recent = await _api.GetInvoicesAsync(1, 10, ct);
            RecentInvoices.Clear();
            foreach (var inv in recent) RecentInvoices.Add(inv);

            var pending = await _localStore.GetPendingOperationsAsync(ct);
            PendingSyncCount = pending.Count;

            IsEmpty = RecentInvoices.Count == 0;
        });
    }

    [RelayCommand]
    public async Task SyncOfflineQueueAsync(CancellationToken ct = default)
    {
        await SafeExecuteAsync(async () =>
        {
            var result = await _syncCoordinator.FlushPendingAsync(ct);
            var remaining = await _localStore.GetPendingOperationsAsync(ct);
            PendingSyncCount = remaining.Count;
            ToastMessage = $"Synced {result.ProcessedCount} bills ({result.ConflictCount} conflicts).";
        });
    }
}
