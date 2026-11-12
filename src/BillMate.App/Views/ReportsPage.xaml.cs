using BillMate.ClientCore.ViewModels;

namespace BillMate.App.Views;

public partial class ReportsPage : ContentPage
{
    private readonly ReportsViewModel _vm;

    public ReportsPage(ReportsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadReportsCommand.ExecuteAsync(null);
    }

    private void OnDayBookTabClicked(object? sender, EventArgs e)
    {
        _vm.SelectedReportTabIndex = 0;
        HighlightButton(sender as Button);
    }

    private void OnGstr1TabClicked(object? sender, EventArgs e)
    {
        _vm.SelectedReportTabIndex = 1;
        HighlightButton(sender as Button);
    }

    private void OnGstr3bTabClicked(object? sender, EventArgs e)
    {
        _vm.SelectedReportTabIndex = 2;
        HighlightButton(sender as Button);
    }

    private void OnExpensesTabClicked(object? sender, EventArgs e)
    {
        _vm.SelectedReportTabIndex = 3;
        HighlightButton(sender as Button);
    }

    private static void HighlightButton(Button? active)
    {
        if (active?.Parent is HorizontalStackLayout stack)
        {
            foreach (var child in stack.Children.OfType<Button>())
            {
                child.BackgroundColor = Color.FromArgb("#1E293B");
                child.TextColor = Color.FromArgb("#CBD5E1");
            }
            active.BackgroundColor = Color.FromArgb("#334155");
            active.TextColor = Color.FromArgb("#FFFFFF");
        }
    }
}
