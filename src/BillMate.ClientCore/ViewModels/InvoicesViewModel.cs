using System.Collections.ObjectModel;
using BillMate.Application.Features.Invoices;
using BillMate.ClientCore.Api;
using BillMate.ClientCore.Services;
using BillMate.Domain.Enums;
using CommunityToolkit.Mvvm.Input;

namespace BillMate.ClientCore.ViewModels;

public sealed class InvoicesViewModel : StatefulViewModelBase
{
    private readonly IBillMateApi _api;
    private readonly ClientSessionState _session;

    public ObservableCollection<InvoiceSummaryDto> Invoices { get; } = [];

    private InvoiceSummaryDto? _selectedInvoice;
    private InvoiceDto? _selectedInvoiceDetail;
    private DateTime _filterFromDate = DateTime.Today.AddDays(-7);
    private DateTime _filterToDate = DateTime.Today;
    private InvoiceStatus? _selectedStatusFilter;
    private string _cancelReason = string.Empty;

    public DateTime FilterFromDate
    {
        get => _filterFromDate;
        set => SetProperty(ref _filterFromDate, value);
    }

    public DateTime FilterToDate
    {
        get => _filterToDate;
        set => SetProperty(ref _filterToDate, value);
    }

    public InvoiceStatus? SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set => SetProperty(ref _selectedStatusFilter, value);
    }

    public InvoiceSummaryDto? SelectedInvoice
    {
        get => _selectedInvoice;
        set
        {
            if (SetProperty(ref _selectedInvoice, value))
            {
                OnPropertyChanged(nameof(HasSelectedInvoice));
                OnPropertyChanged(nameof(CanCancelSelectedInvoice));
            }
        }
    }

    public InvoiceDto? SelectedInvoiceDetail
    {
        get => _selectedInvoiceDetail;
        set
        {
            if (SetProperty(ref _selectedInvoiceDetail, value))
            {
                OnPropertyChanged(nameof(HasSelectedInvoice));
            }
        }
    }

    public string CancelReason
    {
        get => _cancelReason;
        set
        {
            if (SetProperty(ref _cancelReason, value))
            {
                OnPropertyChanged(nameof(CanCancelSelectedInvoice));
            }
        }
    }

    public IAsyncRelayCommand LoadInvoicesCommand { get; }
    public IAsyncRelayCommand<InvoiceSummaryDto?> SelectInvoiceCommand { get; }
    public IAsyncRelayCommand<string?> SetStatusFilterCommand { get; }
    public IAsyncRelayCommand CancelSelectedInvoiceCommand { get; }

    public bool HasSelectedInvoice => SelectedInvoice is not null;

    public bool CanCancelSelectedInvoice =>
        SelectedInvoice is not null
        && SelectedInvoice.Status != InvoiceStatus.Cancelled
        && HasCancelPermission;

    private bool HasCancelPermission =>
        _session.CurrentAuth?.Permissions.Any(p => string.Equals(p, "invoices.cancel", StringComparison.OrdinalIgnoreCase)) == true;

    public InvoicesViewModel(IBillMateApi api, ClientSessionState session)
    {
        _api = api;
        _session = session;

        LoadInvoicesCommand = new AsyncRelayCommand(LoadInvoicesAsync);
        SelectInvoiceCommand = new AsyncRelayCommand<InvoiceSummaryDto?>(invoice => SelectInvoiceAsync(invoice));
        SetStatusFilterCommand = new AsyncRelayCommand<string?>(status => SetStatusFilterAsync(status));
        CancelSelectedInvoiceCommand = new AsyncRelayCommand(CancelSelectedInvoiceAsync);
    }

    public async Task LoadInvoicesAsync(CancellationToken ct = default)
    {
        await SafeExecuteAsync(async () =>
        {
            if (FilterToDate.Date < FilterFromDate.Date)
            {
                FilterToDate = FilterFromDate;
            }

            var from = ToUtcStartOfDay(FilterFromDate);
            var to = ToUtcEndOfDay(FilterToDate);
            var result = await _api.QueryInvoicesAsync(from, to, SelectedStatusFilter, page: 1, pageSize: 100, ct);

            Invoices.Clear();
            foreach (var inv in result.OrderByDescending(i => i.InvoiceDate))
            {
                Invoices.Add(inv);
            }

            IsEmpty = Invoices.Count == 0;

            if (SelectedInvoice is not null)
            {
                var existing = Invoices.FirstOrDefault(i => i.Id == SelectedInvoice.Id);
                SelectedInvoice = existing;
                if (existing is null)
                {
                    SelectedInvoiceDetail = null;
                }
            }
        });
    }

    public async Task SelectInvoiceAsync(InvoiceSummaryDto? invoice, CancellationToken ct = default)
    {
        SelectedInvoice = invoice;

        if (invoice is null)
        {
            SelectedInvoiceDetail = null;
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            SelectedInvoiceDetail = await _api.GetInvoiceByIdAsync(invoice.Id, ct);
        }, showLoading: false);
    }

    public async Task SetStatusFilterAsync(string? statusCode, CancellationToken ct = default)
    {
        SelectedStatusFilter = statusCode?.ToUpperInvariant() switch
        {
            null or "" or "ALL" => null,
            "DRAFT" => InvoiceStatus.Draft,
            "HOLD" => InvoiceStatus.Hold,
            "CONFIRMED" => InvoiceStatus.Confirmed,
            "PARTIAL" => InvoiceStatus.Partial,
            "PAID" => InvoiceStatus.Paid,
            "CANCELLED" => InvoiceStatus.Cancelled,
            _ => null
        };

        await LoadInvoicesAsync(ct);
    }

    public async Task CancelSelectedInvoiceAsync(CancellationToken ct = default)
    {
        if (!CanCancelSelectedInvoice || SelectedInvoice is null)
            return;

        var reason = CancelReason.Trim();
        if (reason.Length < 5)
        {
            SetError("VAL_001", "Please enter a cancellation reason (minimum 5 characters).");
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            await _api.CancelInvoiceAsync(SelectedInvoice.Id, new CancelInvoiceBody(reason), ct);

            ToastMessage = $"Invoice {SelectedInvoice.InvoiceNumber} cancelled.";
            CancelReason = string.Empty;

            await LoadInvoicesAsync(ct);

            var refreshed = Invoices.FirstOrDefault(i => i.Id == SelectedInvoice.Id);
            SelectedInvoice = refreshed;
            if (refreshed is not null)
            {
                SelectedInvoiceDetail = await _api.GetInvoiceByIdAsync(refreshed.Id, ct);
            }
        });
    }

    private static DateTimeOffset ToUtcStartOfDay(DateTime localDate)
    {
        var local = new DateTimeOffset(localDate.Date, TimeZoneInfo.Local.GetUtcOffset(localDate.Date));
        return local.ToUniversalTime();
    }

    private static DateTimeOffset ToUtcEndOfDay(DateTime localDate)
    {
        var endLocal = localDate.Date.AddDays(1).AddTicks(-1);
        var local = new DateTimeOffset(endLocal, TimeZoneInfo.Local.GetUtcOffset(endLocal));
        return local.ToUniversalTime();
    }
}
