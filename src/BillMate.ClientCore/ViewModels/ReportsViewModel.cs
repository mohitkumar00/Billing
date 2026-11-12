using System.Collections.ObjectModel;
using BillMate.Application.Features.Expenses;
using BillMate.Application.Features.Reports;
using BillMate.ClientCore.Api;
using BillMate.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BillMate.ClientCore.ViewModels;

public sealed partial class ReportsViewModel : StatefulViewModelBase
{
    private readonly IBillMateApi _api;

    [ObservableProperty]
    private int _selectedReportTabIndex = 0; // 0 = Day Book, 1 = GSTR-1, 2 = GSTR-3B, 3 = Expenses

    public bool IsDayBookActive => SelectedReportTabIndex == 0;
    public bool IsGstr1Active => SelectedReportTabIndex == 1;
    public bool IsGstr3bActive => SelectedReportTabIndex == 2;
    public bool IsExpensesActive => SelectedReportTabIndex == 3;

    partial void OnSelectedReportTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsDayBookActive));
        OnPropertyChanged(nameof(IsGstr1Active));
        OnPropertyChanged(nameof(IsGstr3bActive));
        OnPropertyChanged(nameof(IsExpensesActive));
    }

    [ObservableProperty]
    private DayBookReportDto? _dayBook;

    [ObservableProperty]
    private Gstr1ReportDto? _gstr1;

    [ObservableProperty]
    private Gstr3bReportDto? _gstr3b;

    [ObservableProperty]
    private SalesSummaryDto? _salesSummary;

    public ObservableCollection<ExpenseDto> Expenses { get; } = [];
    public ObservableCollection<ExpenseCategoryDto> Categories { get; } = [];

    // Record Expense Form Fields
    [ObservableProperty]
    private decimal _newExpenseAmount;

    [ObservableProperty]
    private string _newExpenseDescription = string.Empty;

    [ObservableProperty]
    private Guid _selectedExpenseCategoryId;

    [ObservableProperty]
    private int _selectedCategoryIndex = 0;

    [ObservableProperty]
    private PaymentMode _newExpensePaymentMode = PaymentMode.Cash;

    [ObservableProperty]
    private bool _isAddExpenseDialogOpen;

    public ReportsViewModel(IBillMateApi api)
    {
        _api = api;
    }

    [RelayCommand]
    public async Task LoadReportsAsync(CancellationToken ct = default)
    {
        await SafeExecuteAsync(async () =>
        {
            var istNow = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5));
            var startOfDay = new DateTimeOffset(istNow.Year, istNow.Month, istNow.Day, 0, 0, 0, istNow.Offset).ToUniversalTime();
            var endOfDay = startOfDay.AddDays(1).AddTicks(-1);

            // 1. Day Book
            try
            {
                var todayStr = istNow.ToString("yyyy-MM-dd");
                DayBook = await _api.GetDayBookAsync(todayStr, ct);
            }
            catch { }

            // 2. Sales Summary
            try
            {
                SalesSummary = await _api.GetSalesSummaryAsync(startOfDay, endOfDay, ct);
            }
            catch { }

            // 3. GSTR-1
            try
            {
                Gstr1 = await _api.GetGstr1ReportAsync(startOfDay, endOfDay, ct);
            }
            catch { }

            // 4. GSTR-3B
            try
            {
                Gstr3b = await _api.GetGstr3bSummaryAsync(startOfDay, endOfDay, ct);
            }
            catch { }

            // 5. Expenses & Categories
            try
            {
                var cats = await _api.GetExpenseCategoriesAsync(ct);
                Categories.Clear();
                foreach (var c in cats) Categories.Add(c);
                if (Categories.Count > 0 && SelectedExpenseCategoryId == Guid.Empty)
                    SelectedExpenseCategoryId = Categories[0].Id;

                var exps = await _api.GetExpensesAsync(startOfDay, endOfDay, null, ct);
                Expenses.Clear();
                foreach (var e in exps) Expenses.Add(e);
            }
            catch { }
        });
    }

    [RelayCommand]
    public void OpenAddExpenseDialog()
    {
        NewExpenseAmount = 0m;
        NewExpenseDescription = string.Empty;
        NewExpensePaymentMode = PaymentMode.Cash;
        if (Categories.Count > 0) SelectedExpenseCategoryId = Categories[0].Id;
        IsAddExpenseDialogOpen = true;
    }

    [RelayCommand]
    public void CloseAddExpenseDialog()
    {
        IsAddExpenseDialogOpen = false;
    }

    [RelayCommand]
    public async Task SubmitExpenseAsync(CancellationToken ct = default)
    {
        if (NewExpenseAmount <= 0)
        {
            ToastMessage = "Expense amount must be greater than zero.";
            return;
        }

        if (Categories.Count > 0 && SelectedCategoryIndex >= 0 && SelectedCategoryIndex < Categories.Count)
            SelectedExpenseCategoryId = Categories[SelectedCategoryIndex].Id;
        else if (SelectedExpenseCategoryId == Guid.Empty && Categories.Count > 0)
            SelectedExpenseCategoryId = Categories[0].Id;

        await SafeExecuteAsync(async () =>
        {
            var cmd = new CreateExpenseCommand(
                ExpenseCategoryId: SelectedExpenseCategoryId,
                Amount: NewExpenseAmount,
                PaymentMode: NewExpensePaymentMode,
                Description: string.IsNullOrWhiteSpace(NewExpenseDescription) ? null : NewExpenseDescription.Trim());

            var created = await _api.CreateExpenseAsync(cmd, ct);
            ToastMessage = $"Expense of ₹{created.Amount:N2} recorded under '{created.CategoryName}'.";
            IsAddExpenseDialogOpen = false;

            await LoadReportsAsync(ct);
        });
    }
}
