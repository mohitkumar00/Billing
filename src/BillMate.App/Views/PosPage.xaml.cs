using BillMate.Application.Features.Products;
using BillMate.ClientCore.ViewModels;

namespace BillMate.App.Views;

public partial class PosPage : ContentPage
{
    private readonly PosViewModel _vm;

    public PosPage(PosViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _vm.SearchCatalogCommand.ExecuteAsync(null);

        Dispatcher.Dispatch(async () =>
        {
            await Task.Delay(150);
            try
            {
                BarcodeEntry?.Focus();
            }
            catch { }
        });
    }

    private void OnProductTapped(object? sender, EventArgs e)
    {
        if (sender is VisualElement { BindingContext: ProductDto prod })
        {
            _vm.AddToCartCommand.Execute(prod);
        }
    }

    private void OnIncrementQuantityClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: CartLineViewModel line })
        {
            _vm.IncrementQuantityCommand.Execute(line);
        }
    }

    private void OnDecrementQuantityClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: CartLineViewModel line })
        {
            _vm.DecrementQuantityCommand.Execute(line);
        }
    }
}
