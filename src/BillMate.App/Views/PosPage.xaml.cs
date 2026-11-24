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

    private async void OnShareLastBillWhatsAppClicked(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_vm.LastWhatsAppUrl))
        {
            try
            {
                await Microsoft.Maui.ApplicationModel.Launcher.OpenAsync(new Uri(_vm.LastWhatsAppUrl));
            }
            catch (Exception ex)
            {
                await DisplayAlert("WhatsApp Launch", $"Could not open WhatsApp: {ex.Message}", "OK");
            }
        }
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

    private async void OnItemDiscountClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: CartLineViewModel line }) return;

        var currentVal = line.DiscountAmount > 0 ? line.DiscountAmount.ToString("0.##") : string.Empty;
        var result = await DisplayPromptAsync(
            "Item Discount",
            $"Enter discount for '{line.Product.Name}':\n(e.g., 20 for ₹20, or 10% for 10% off)",
            accept: "Apply",
            cancel: "Cancel",
            placeholder: "0",
            initialValue: currentVal,
            keyboard: Keyboard.Text);

        if (result is null) return;

        var trimmed = result.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            _vm.SetLineDiscount(line, 0m);
            return;
        }

        decimal discountAmount = 0m;
        if (trimmed.EndsWith('%'))
        {
            var pctStr = trimmed.TrimEnd('%').Trim();
            if (decimal.TryParse(pctStr, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var pct) ||
                decimal.TryParse(pctStr, out pct))
            {
                var rawLineTotal = line.Quantity * line.Product.SellingPrice;
                discountAmount = Math.Round(rawLineTotal * (pct / 100m), 2);
            }
        }
        else
        {
            if (decimal.TryParse(trimmed, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amt) ||
                decimal.TryParse(trimmed, out amt))
            {
                discountAmount = amt;
            }
        }

        _vm.SetLineDiscount(line, Math.Max(0m, discountAmount));
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
