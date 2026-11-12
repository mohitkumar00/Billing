using BillMate.Application.Features.Products;
using BillMate.ClientCore.ViewModels;

namespace BillMate.App.Views;

public partial class InventoryPage : ContentPage
{
    private readonly InventoryViewModel _vm;

    public InventoryPage(InventoryViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadInventoryCommand.ExecuteAsync(null);
    }

    private void OnFilterAllClicked(object? sender, EventArgs e)
    {
        _vm.SelectedFilterIndex = 0;
    }

    private void OnFilterLowStockClicked(object? sender, EventArgs e)
    {
        _vm.SelectedFilterIndex = 1;
    }

    private void OnFilterOutOfStockClicked(object? sender, EventArgs e)
    {
        _vm.SelectedFilterIndex = 2;
    }

    private async void OnQuickAddClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: ProductDto prod })
        {
            await _vm.QuickAddStockCommand.ExecuteAsync(prod);
        }
    }

    private async void OnQuickDeductClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: ProductDto prod })
        {
            await _vm.QuickDeductStockCommand.ExecuteAsync(prod);
        }
    }

    private void OnAdjustClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: ProductDto prod })
        {
            _vm.OpenAdjustStockCommand.Execute(prod);
        }
    }

    private async void OnPickImageClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select Product Image (JPG/PNG)",
                FileTypes = FilePickerFileType.Images
            });
            if (result != null)
            {
                _vm.SetProductImage(result.FullPath);
            }
        }
        catch { }
    }
}
