using BillMate.Application.Features.Invoices;
using BillMate.ClientCore.ViewModels;

namespace BillMate.App.Views;

public partial class InvoicesPage : ContentPage
{
    private readonly InvoicesViewModel _vm;

    public InvoicesPage(InvoicesViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadInvoicesCommand.ExecuteAsync(null);
    }

    private async void OnInvoiceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selected = e.CurrentSelection?.FirstOrDefault() as InvoiceSummaryDto;
        await _vm.SelectInvoiceCommand.ExecuteAsync(selected);
    }
}
