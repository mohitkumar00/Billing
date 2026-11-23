using BillMate.Application.Features.Products;
using BillMate.ClientCore.ViewModels;
#if WINDOWS
using Microsoft.UI.Xaml;
using Windows.System;
#endif

namespace BillMate.App.Views;

public partial class PosPage : ContentPage
{
    private readonly PosViewModel _vm;
#if WINDOWS
    private FrameworkElement? _platformRoot;
#endif

    public PosPage(PosViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if WINDOWS
        DetachWindowsKeyHandler();
        AttachWindowsKeyHandler();
#endif
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

#if WINDOWS
    private void AttachWindowsKeyHandler()
    {
        _platformRoot = Handler?.PlatformView as FrameworkElement;
        if (_platformRoot is not null)
        {
            _platformRoot.KeyDown += OnWindowsKeyDown;
        }
    }

    private void DetachWindowsKeyHandler()
    {
        if (_platformRoot is not null)
        {
            _platformRoot.KeyDown -= OnWindowsKeyDown;
            _platformRoot = null;
        }
    }

    private void OnWindowsKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.F2:
                BarcodeEntry?.Focus();
                e.Handled = true;
                break;
            case VirtualKey.F4:
                _vm.HoldCurrentBillCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.F6:
                _vm.ClearCurrentBillCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.F8:
                _ = _vm.ReprintLastSavedPdfCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case VirtualKey.F9:
                _ = _vm.CheckoutCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
        }
    }
#endif
}
