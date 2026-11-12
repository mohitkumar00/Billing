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
    private string _customerPhone = string.Empty;

    [ObservableProperty]
    private string _customerName = string.Empty;

    [ObservableProperty]
    private string _customerGstin = string.Empty;

    [ObservableProperty]
    private decimal _billDiscountAmount;

    [ObservableProperty]
    private bool _isHeldBillsDrawerOpen;

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
    public void SelectPaymentMode(PaymentMode mode)
    {
        SelectedPaymentMode = mode;
        if (mode == PaymentMode.Cash && CashTendered < GrandTotal)
        {
            CashTendered = GrandTotal;
        }
        OnPropertyChanged(nameof(ChangeToReturn));
    }

    [RelayCommand]
    public void SetQuickCashTendered(decimal amount)
    {
        CashTendered = amount;
        OnPropertyChanged(nameof(ChangeToReturn));
    }

    [RelayCommand]
    public void ToggleHeldBillsDrawer()
    {
        IsHeldBillsDrawerOpen = !IsHeldBillsDrawerOpen;
    }

    [RelayCommand]
    public async Task SearchCatalogAsync(CancellationToken ct = default)
    {
        await SafeExecuteAsync(async () =>
        {
            var syncContext = SynchronizationContext.Current;

            // 1. Instant local SQLite cache lookup (< 10ms, never hangs UI)
            var cached = await _localStore.SearchCachedProductsAsync(SearchQuery, 60, ct);
            CatalogProducts.Clear();
            foreach (var p in cached) CatalogProducts.Add(p);
            IsEmpty = CatalogProducts.Count == 0;

            // 2. Fetch fresh catalog from API in background without blocking POS UI
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var remote = await _api.SearchProductsAsync(SearchQuery, 60, cts.Token);
                    if (remote.Count > 0)
                    {
                        await _localStore.UpsertProductsAsync(remote, cts.Token);
                        void UpdateList()
                        {
                            IsOfflineMode = false;
                            CatalogProducts.Clear();
                            foreach (var p in remote) CatalogProducts.Add(p);
                            IsEmpty = CatalogProducts.Count == 0;
                        }

                        if (syncContext != null)
                        {
                            syncContext.Post(_ => UpdateList(), null);
                        }
                        else
                        {
                            UpdateList();
                        }
                    }
                }
                catch
                {
                    if (syncContext != null)
                    {
                        syncContext.Post(_ => IsOfflineMode = true, null);
                    }
                    else
                    {
                        IsOfflineMode = true;
                    }
                }
            });

            // Instant Barcode Scanner: If exact barcode or SKU match, automatically add to cart
            var trimmed = SearchQuery.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && cached.Count > 0)
            {
                var exact = cached.FirstOrDefault(p =>
                    string.Equals(p.Barcode, trimmed, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Sku, trimmed, StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                {
                    AddToCart(exact);
                    SearchQuery = string.Empty;
                }
            }
        }, showLoading: false);
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

            Guid? customerId = null;
            if (!string.IsNullOrWhiteSpace(CustomerPhone))
            {
                try
                {
                    var found = await _api.SearchCustomersAsync(CustomerPhone.Trim(), 1, ct);
                    if (found.Count > 0)
                    {
                        customerId = found[0].Id;
                    }
                    else if (!string.IsNullOrWhiteSpace(CustomerName))
                    {
                        var createdCustomer = await _api.CreateCustomerAsync(new Application.Features.Parties.CreateCustomerCommand(
                            Name: CustomerName.Trim(),
                            Phone: CustomerPhone.Trim(),
                            Gstin: string.IsNullOrWhiteSpace(CustomerGstin) ? null : CustomerGstin.Trim(),
                            StateCode: CustomerGstin?.Length >= 2 ? CustomerGstin[..2] : null), ct);
                        customerId = createdCustomer.Id;
                    }
                }
                catch { }
            }

            var command = new CreateInvoiceCommand(
                IdempotencyKey: idempotencyKey,
                Items: lines,
                Payments: payments,
                CustomerId: customerId,
                PlaceOfSupplyCode: CustomerGstin?.Length >= 2 ? CustomerGstin[..2] : _session.SellerStateCode,
                Notes: BillDiscountAmount > 0 ? $"Special bill discount ₹{BillDiscountAmount:N2} applied" : null);

            try
            {
                var saved = await _api.CreateInvoiceAsync(command, ct);
                IsOfflineMode = false;
                LastReceiptNumber = saved.InvoiceNumber;
                ToastMessage = $"Bill {saved.InvoiceNumber} saved (₹{saved.GrandTotal:N2}). Mode: {SelectedPaymentMode}.";
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
            BillDiscountAmount = 0m;
            CustomerPhone = string.Empty;
            CustomerName = string.Empty;
            CustomerGstin = string.Empty;
            RecalculateCart();
        }, showLoading: false);
    }

    public void RecalculateCart()
    {
        var isInterState = !string.IsNullOrWhiteSpace(CustomerGstin) && CustomerGstin.Length >= 2 && !CustomerGstin.StartsWith(_session.SellerStateCode);
        foreach (var line in CartLines)
            line.Recalculate(isInterState, _session.IsCompositionScheme);

        TaxableTotal = CartLines.Sum(l => l.TaxableAmount);
        GstTotal = CartLines.Sum(l => l.TaxAmount);
        var subTotal = CartLines.Sum(l => l.LineTotal);
        var afterDiscount = Math.Max(0m, subTotal - BillDiscountAmount);
        var (rounded, delta) = new Money(afterDiscount).RoundToNearestRupee();
        RoundOff = delta;
        GrandTotal = rounded.Amount;

        if (SelectedPaymentMode == PaymentMode.Cash && CashTendered < GrandTotal)
        {
            CashTendered = GrandTotal;
        }

        OnPropertyChanged(nameof(ChangeToReturn));
    }
}
