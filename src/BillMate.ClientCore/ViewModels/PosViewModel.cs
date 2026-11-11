using System.Collections.ObjectModel;
using BillMate.Application.Features.Invoices;
using BillMate.Application.Features.Products;
using BillMate.ClientCore.Api;
using BillMate.ClientCore.Offline;
using BillMate.ClientCore.Services;
using BillMate.Domain.Enums;
using BillMate.Domain.Services;
using BillMate.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BillMate.ClientCore.ViewModels;

public sealed partial class CartLineViewModel : ObservableObject
{
    public ProductDto Product { get; }

    [ObservableProperty]
    private decimal _quantity;

    [ObservableProperty]
    private decimal _discountAmount;

    [ObservableProperty]
    private decimal _taxableAmount;

    [ObservableProperty]
    private decimal _taxAmount;

    [ObservableProperty]
    private decimal _lineTotal;

    public CartLineViewModel(ProductDto product, decimal quantity = 1m)
    {
        Product = product;
        _quantity = quantity;
    }

    public void Recalculate(bool isInterState, bool isComposition)
    {
        var breakdown = GstCalculator.CalculateLine(
            Quantity, Product.SellingPrice, DiscountAmount, Product.GstRate, 0m,
            Product.IsTaxInclusive, isInterState, isComposition);
        TaxableAmount = breakdown.TaxableAmount;
        TaxAmount = breakdown.TotalTax;
        LineTotal = breakdown.LineTotal;
    }
}

public sealed record HeldCartSnapshot(Guid HoldId, string Label, DateTimeOffset HeldAt, IReadOnlyList<CartLineViewModel> Lines);

public sealed partial class PosViewModel : StatefulViewModelBase
{
    private readonly IBillMateApi _api;
    private readonly OfflineLocalStore _localStore;
    private readonly ClientSessionState _session;

    public ObservableCollection<ProductDto> CatalogProducts { get; } = [];
    public ObservableCollection<CartLineViewModel> CartLines { get; } = [];
    public ObservableCollection<HeldCartSnapshot> HeldBills { get; } = [];

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isOfflineMode;

    [ObservableProperty]
    private decimal _taxableTotal;

    [ObservableProperty]
    private decimal _gstTotal;

    [ObservableProperty]
    private decimal _roundOff;

    [ObservableProperty]
    private decimal _grandTotal;

    [ObservableProperty]
    private PaymentMode _selectedPaymentMode = PaymentMode.Cash;

    [ObservableProperty]
    private decimal _cashTendered;

    [ObservableProperty]
    private string? _lastReceiptNumber;

    public decimal ChangeToReturn => Math.Max(0m, CashTendered - GrandTotal);

    public PosViewModel(IBillMateApi api, OfflineLocalStore localStore, ClientSessionState session)
    {
        _api = api;
        _localStore = localStore;
        _session = session;
    }

    [RelayCommand]
    public async Task SearchCatalogAsync(CancellationToken ct = default)
    {
        await SafeExecuteAsync(async () =>
        {
            CatalogProducts.Clear();
            try
            {
                var remote = await _api.SearchProductsAsync(SearchQuery, 60, ct);
                IsOfflineMode = false;
                await _localStore.UpsertProductsAsync(remote, ct);
                foreach (var p in remote) CatalogProducts.Add(p);
            }
            catch
            {
                IsOfflineMode = true;
                var cached = await _localStore.SearchCachedProductsAsync(SearchQuery, 60, ct);
                foreach (var p in cached) CatalogProducts.Add(p);
            }

            IsEmpty = CatalogProducts.Count == 0;
        });
    }

    [RelayCommand]
    public void AddToCart(ProductDto product)
    {
        var existing = CartLines.FirstOrDefault(l => l.Product.Id == product.Id);
        if (existing is not null)
        {
            existing.Quantity += 1m;
        }
        else
        {
            CartLines.Add(new CartLineViewModel(product, 1m));
        }
        RecalculateCart();
    }

    [RelayCommand]
    public void IncrementQuantity(CartLineViewModel line)
    {
        line.Quantity += 1m;
        RecalculateCart();
    }

    [RelayCommand]
    public void DecrementQuantity(CartLineViewModel line)
    {
        if (line.Quantity <= 1m)
            CartLines.Remove(line);
        else
            line.Quantity -= 1m;
        RecalculateCart();
    }

    [RelayCommand]
    public void HoldCurrentBill(string? label = null)
    {
        if (CartLines.Count == 0) return;
        var holdLabel = string.IsNullOrWhiteSpace(label) ? $"Hold #{HeldBills.Count + 1}" : label.Trim();
        HeldBills.Add(new HeldCartSnapshot(Guid.NewGuid(), holdLabel, DateTimeOffset.UtcNow, CartLines.ToArray()));
        CartLines.Clear();
        RecalculateCart();
        ToastMessage = $"Bill parked as '{holdLabel}'.";
    }

    [RelayCommand]
    public void ResumeHeldBill(HeldCartSnapshot held)
    {
        HeldBills.Remove(held);
        CartLines.Clear();
        foreach (var line in held.Lines) CartLines.Add(line);
        RecalculateCart();
    }

    [RelayCommand]
    public async Task CheckoutAsync(CancellationToken ct = default)
    {
        if (CartLines.Count == 0) return;

        await SafeExecuteAsync(async () =>
        {
            var idempotencyKey = Guid.NewGuid();
            var lines = CartLines.Select(l => new CreateInvoiceLineInput(l.Product.Id, l.Quantity, l.Product.SellingPrice, l.DiscountAmount)).ToArray();
            var payments = GrandTotal > 0m ? new[] { new InvoicePaymentInput(SelectedPaymentMode, GrandTotal) } : Array.Empty<InvoicePaymentInput>();
            var command = new CreateInvoiceCommand(idempotencyKey, lines, payments);

            try
            {
                var saved = await _api.CreateInvoiceAsync(command, ct);
                IsOfflineMode = false;
                LastReceiptNumber = saved.InvoiceNumber;
                ToastMessage = $"Bill {saved.InvoiceNumber} saved (₹{saved.GrandTotal:N2}).";
            }
            catch
            {
                // Offline-first fallback: persist into SQLite WAL pending_sync_queue
                IsOfflineMode = true;
                await _localStore.EnqueueOfflineInvoiceAsync(command, ct);
                LastReceiptNumber = $"OFF-{idempotencyKey.ToString("N")[..6].ToUpperInvariant()}";
                ToastMessage = $"Saved offline as {LastReceiptNumber}. Will sync on LAN reconnect.";
            }

            CartLines.Clear();
            CashTendered = 0m;
            RecalculateCart();
        }, showLoading: false);
    }

    public void RecalculateCart()
    {
        foreach (var line in CartLines)
            line.Recalculate(isInterState: false, _session.IsCompositionScheme);

        TaxableTotal = CartLines.Sum(l => l.TaxableAmount);
        GstTotal = CartLines.Sum(l => l.TaxAmount);
        var (rounded, delta) = new Money(CartLines.Sum(l => l.LineTotal)).RoundToNearestRupee();
        RoundOff = delta;
        GrandTotal = rounded.Amount;
        OnPropertyChanged(nameof(ChangeToReturn));
    }
}
