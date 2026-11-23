using System.Collections.ObjectModel;
using System.Windows.Input;
using BillMate.Application.Common.Interfaces;
using BillMate.Application.Features.Parties;
using BillMate.ClientCore.Api;
using BillMate.ClientCore.Services;
using BillMate.ClientCore.Theme;
using BillMate.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BillMate.ClientCore.ViewModels;

public sealed partial class CustomerKhataItem : ObservableObject
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public decimal CreditLimit { get; init; }
    
    [ObservableProperty]
    private decimal _outstandingBalance;

    [ObservableProperty]
    private bool _hasBalanceDue;

    public int LoyaltyPoints { get; init; }
}

public sealed partial class KhataViewModel : ObservableObject
{
    private readonly IBillMateApi _api;
    private readonly WhiteLabelConfig _config;

    [ObservableProperty]
    private ObservableCollection<CustomerKhataItem> _customers = [];

    [ObservableProperty]
    private ObservableCollection<CustomerKhataItem> _filteredCustomers = [];

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private decimal _totalOutstanding;

    [ObservableProperty]
    private int _customersWithDueCount;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private CustomerKhataItem? _selectedCustomer;

    // Payment Settlement Modal Properties
    [ObservableProperty]
    private bool _isPaymentModalOpen;

    [ObservableProperty]
    private decimal _settlementAmount;

    [ObservableProperty]
    private PaymentMode _settlementPaymentMode = PaymentMode.Cash;

    [ObservableProperty]
    private string _settlementNotes = string.Empty;

    public KhataViewModel(IBillMateApi api, WhiteLabelConfig config)
    {
        _api = api;
        _config = config;
    }

    [RelayCommand]
    public async Task LoadCustomersAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading Khata ledger...";
        try
        {
            var list = await _api.SearchCustomersAsync(limit: 200);
            Customers.Clear();
            foreach (var c in list)
            {
                Customers.Add(new CustomerKhataItem
                {
                    Id = c.Id,
                    Name = c.Name,
                    Phone = c.Phone,
                    CreditLimit = c.CreditLimit,
                    OutstandingBalance = c.OutstandingBalance,
                    HasBalanceDue = c.OutstandingBalance > 0m,
                    LoyaltyPoints = c.LoyaltyPoints
                });
            }

            ApplyFilter();
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var term = SearchQuery.Trim().ToLowerInvariant();
        var matches = string.IsNullOrWhiteSpace(term)
            ? Customers
            : new ObservableCollection<CustomerKhataItem>(
                Customers.Where(c => c.Name.ToLowerInvariant().Contains(term) || c.Phone.Contains(term)));

        FilteredCustomers = matches;
        TotalOutstanding = Customers.Sum(c => c.OutstandingBalance);
        CustomersWithDueCount = Customers.Count(c => c.OutstandingBalance > 0m);
    }

    [RelayCommand]
    public void OpenPaymentModal(CustomerKhataItem customer)
    {
        SelectedCustomer = customer;
        SettlementAmount = customer.OutstandingBalance;
        SettlementPaymentMode = PaymentMode.Cash;
        SettlementNotes = "Khata settlement";
        IsPaymentModalOpen = true;
    }

    [RelayCommand]
    public void ClosePaymentModal()
    {
        IsPaymentModalOpen = false;
        SelectedCustomer = null;
    }

    [RelayCommand]
    public async Task SubmitPaymentAsync()
    {
        if (SelectedCustomer == null || SettlementAmount <= 0m) return;

        IsLoading = true;
        StatusMessage = "Recording payment...";
        try
        {
            var body = new CustomerPaymentBody(
                InvoiceId: null,
                PaymentMode: SettlementPaymentMode,
                Amount: SettlementAmount,
                ReferenceNumber: null,
                Notes: SettlementNotes);

            var updated = await _api.RecordCustomerPaymentAsync(SelectedCustomer.Id, body);

            SelectedCustomer.OutstandingBalance = updated.OutstandingBalance;
            SelectedCustomer.HasBalanceDue = updated.OutstandingBalance > 0m;
            TotalOutstanding = Customers.Sum(c => c.OutstandingBalance);
            CustomersWithDueCount = Customers.Count(c => c.OutstandingBalance > 0m);

            ClosePaymentModal();
            StatusMessage = $"Successfully received ₹{SettlementAmount:N2} from {SelectedCustomer.Name}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Payment failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public string GetWhatsAppReminderUrl(CustomerKhataItem customer)
    {
        return WhatsAppShareHelper.BuildKhataReminderUrl(
            customerPhone: customer.Phone,
            customerName: customer.Name,
            businessName: _config.BusinessName,
            outstandingBalance: customer.OutstandingBalance,
            upiVpa: _config.GstinDefault // Or VPA configured
        );
    }
}
