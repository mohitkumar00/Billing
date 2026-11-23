using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using BillMate.Application.Common.Interfaces;
using BillMate.Application.Features.Invoices;
using BillMate.Application.Features.Parties;
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

public sealed partial class SplitPaymentLineViewModel : ObservableObject
{
    [ObservableProperty]
    private PaymentMode _mode;

    [ObservableProperty]
    private decimal _amount;

    public SplitPaymentLineViewModel(PaymentMode mode, decimal amount)
    {
        _mode = mode;
        _amount = amount;
    }
}

public sealed partial class PosViewModel : StatefulViewModelBase
{
    private static readonly string PdfSaveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BillMate", "Invoices", "PDF");

    private readonly IBillMateApi _api;
    private readonly OfflineLocalStore _localStore;
    private readonly ClientSessionState _session;
    private CancellationTokenSource? _customerLookupCts;

    public ObservableCollection<ProductDto> CatalogProducts { get; } = [];
    public ObservableCollection<CartLineViewModel> CartLines { get; } = [];
    public ObservableCollection<HeldCartSnapshot> HeldBills { get; } = [];
    public ObservableCollection<CustomerDto> CustomerSuggestions { get; } = [];
    public ObservableCollection<SplitPaymentLineViewModel> SplitPayments { get; } = [];

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
    private Guid? _selectedCustomerId;

    [ObservableProperty]
    private bool _isCustomerSuggestionsOpen;

    [ObservableProperty]
    private string _billNotes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalSplitPaymentAmount))]
    [NotifyPropertyChangedFor(nameof(RemainingSplitAmount))]
    private bool _isSplitPaymentEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalSplitPaymentAmount))]
    [NotifyPropertyChangedFor(nameof(RemainingSplitAmount))]
    private decimal _splitCashAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalSplitPaymentAmount))]
    [NotifyPropertyChangedFor(nameof(RemainingSplitAmount))]
    private decimal _splitUpiAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalSplitPaymentAmount))]
    [NotifyPropertyChangedFor(nameof(RemainingSplitAmount))]
    private decimal _splitCardAmount;

    [ObservableProperty]
    private decimal _billDiscountAmount;

    [ObservableProperty]
    private bool _isHeldBillsDrawerOpen;

    [ObservableProperty]
    private string? _lastReceiptNumber;

    [ObservableProperty]
    private string? _lastSavedPdfPath;

    [ObservableProperty]
    private string? _lastWhatsAppUrl;

    [ObservableProperty]
    private bool _hasWhatsAppShare;

    [ObservableProperty]
    private string _upiQrUri = string.Empty;

    [ObservableProperty]
    private bool _isUpiPaymentActive;

    public decimal ChangeToReturn => Math.Max(0m, CashTendered - GrandTotal);
    public decimal TotalSplitPaymentAmount => IsSplitPaymentEnabled
        ? SplitCashAmount + SplitUpiAmount + SplitCardAmount
        : SplitPayments.Sum(x => x.Amount);
    public decimal RemainingSplitAmount => Math.Max(0m, GrandTotal - TotalSplitPaymentAmount);

    public PosViewModel(IBillMateApi api, OfflineLocalStore localStore, ClientSessionState session)
    {
        _api = api;
        _localStore = localStore;
        _session = session;
    }

    [RelayCommand]
    public void SelectPaymentMode(object? mode)
    {
        var parsedMode = mode switch
        {
            PaymentMode paymentMode => paymentMode,
            string s when Enum.TryParse<PaymentMode>(s, true, out var fromString) => fromString,
            _ => SelectedPaymentMode
        };

        if (parsedMode == SelectedPaymentMode)
        {
            return;
        }

        SelectedPaymentMode = parsedMode;
        if (parsedMode == PaymentMode.Cash && CashTendered < GrandTotal)
        {
            CashTendered = GrandTotal;
        }
        OnPropertyChanged(nameof(ChangeToReturn));
    }

    [RelayCommand]
    public void UpdateSplitPaymentAmount((SplitPaymentLineViewModel line, object? amount) payload)
    {
        if (payload.line is null)
        {
            return;
        }

        var parsedAmount = payload.amount switch
        {
            decimal d => d,
            double d => (decimal)d,
            float f => (decimal)f,
            int i => i,
            long l => l,
            string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) => value,
            string s when decimal.TryParse(s, out var value) => value,
            _ => 0m
        };

        payload.line.Amount = Math.Max(0m, parsedAmount);
        OnPropertyChanged(nameof(TotalSplitPaymentAmount));
        OnPropertyChanged(nameof(RemainingSplitAmount));
    }

    [RelayCommand]
    public void SetQuickCashTendered(object? amount)
    {
        var parsedAmount = amount switch
        {
            decimal d => d,
            double d => (decimal)d,
            float f => (decimal)f,
            int i => i,
            long l => l,
            string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) => value,
            string s when decimal.TryParse(s, out var value) => value,
            _ => 0m
        };

        if (parsedAmount <= 0m)
        {
            return;
        }

        CashTendered = parsedAmount;
        OnPropertyChanged(nameof(ChangeToReturn));
    }

    [RelayCommand]
    public void ToggleHeldBillsDrawer()
    {
        IsHeldBillsDrawerOpen = !IsHeldBillsDrawerOpen;
    }

    [RelayCommand]
    public void ToggleSplitPayment()
    {
        IsSplitPaymentEnabled = !IsSplitPaymentEnabled;
        if (IsSplitPaymentEnabled)
        {
            SplitCashAmount = GrandTotal;
            SplitUpiAmount = 0m;
            SplitCardAmount = 0m;
            if (SplitPayments.Count == 0)
            {
                SplitPayments.Add(new SplitPaymentLineViewModel(PaymentMode.Cash, GrandTotal));
            }
        }
        else
        {
            SplitCashAmount = 0m;
            SplitUpiAmount = 0m;
            SplitCardAmount = 0m;
            SplitPayments.Clear();
        }

        OnPropertyChanged(nameof(TotalSplitPaymentAmount));
        OnPropertyChanged(nameof(RemainingSplitAmount));
    }

    [RelayCommand]
    public void AddSplitPaymentLine()
    {
        if (!IsSplitPaymentEnabled || SplitPayments.Count >= 3)
        {
            return;
        }

        var defaultMode = SplitPayments.Any(x => x.Mode == PaymentMode.Upi) ? PaymentMode.Card : PaymentMode.Upi;
        SplitPayments.Add(new SplitPaymentLineViewModel(defaultMode, 0m));
        OnPropertyChanged(nameof(TotalSplitPaymentAmount));
        OnPropertyChanged(nameof(RemainingSplitAmount));
    }

    [RelayCommand]
    public void RemoveSplitPaymentLine(SplitPaymentLineViewModel line)
    {
        if (!IsSplitPaymentEnabled || SplitPayments.Count <= 1)
        {
            return;
        }

        SplitPayments.Remove(line);
        OnPropertyChanged(nameof(TotalSplitPaymentAmount));
        OnPropertyChanged(nameof(RemainingSplitAmount));
    }

    [RelayCommand]
    public void SetSplitPaymentMode((SplitPaymentLineViewModel line, string mode) payload)
    {
        if (payload.line is null || string.IsNullOrWhiteSpace(payload.mode))
        {
            return;
        }

        if (Enum.TryParse<PaymentMode>(payload.mode, true, out var parsed))
        {
            payload.line.Mode = parsed;
        }
    }

    partial void OnCustomerPhoneChanged(string value)
    {
        _ = LoadCustomerSuggestionsAsync(value);
    }

    partial void OnCustomerNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(CustomerPhone))
        {
            _ = LoadCustomerSuggestionsAsync(value);
        }
    }

    [RelayCommand]
    public async Task LoadCustomerSuggestionsAsync(string? query = null)
    {
        var searchText = string.IsNullOrWhiteSpace(query)
            ? (string.IsNullOrWhiteSpace(CustomerPhone) ? CustomerName : CustomerPhone)
            : query.Trim();

        _customerLookupCts?.Cancel();
        _customerLookupCts?.Dispose();
        _customerLookupCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var ct = _customerLookupCts.Token;

        if (string.IsNullOrWhiteSpace(searchText) || searchText.Length < 3)
        {
            CustomerSuggestions.Clear();
            IsCustomerSuggestionsOpen = false;
            return;
        }

        try
        {
            var matches = await _api.SearchCustomersAsync(searchText, 8, ct);
            CustomerSuggestions.Clear();
            foreach (var c in matches)
            {
                CustomerSuggestions.Add(c);
            }

            IsCustomerSuggestionsOpen = CustomerSuggestions.Count > 0;
        }
        catch
        {
            CustomerSuggestions.Clear();
            IsCustomerSuggestionsOpen = false;
        }
    }

    [RelayCommand]
    public void SelectCustomerSuggestion(CustomerDto customer)
    {
        SelectedCustomerId = customer.Id;
        CustomerPhone = customer.Phone;
        CustomerName = customer.Name;
        CustomerGstin = customer.Gstin ?? string.Empty;
        CustomerSuggestions.Clear();
        IsCustomerSuggestionsOpen = false;
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
    public void ClearCurrentBill()
    {
        CartLines.Clear();
        BillDiscountAmount = 0m;
        CashTendered = 0m;
        SelectedCustomerId = null;
        CustomerPhone = string.Empty;
        CustomerName = string.Empty;
        CustomerGstin = string.Empty;
        BillNotes = string.Empty;
        CustomerSuggestions.Clear();
        IsCustomerSuggestionsOpen = false;
        RecalculateCart();
        ToastMessage = "Current bill cleared.";
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
            var payments = BuildPayments();
            if (payments.Count == 0)
            {
                ToastMessage = "Please provide payment details before saving bill.";
                return;
            }

            Guid? customerId = SelectedCustomerId;
            if (!customerId.HasValue && !string.IsNullOrWhiteSpace(CustomerPhone))
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
                Notes: BuildInvoiceNotes());

            try
            {
                var saved = await _api.CreateInvoiceAsync(command, ct);
                IsOfflineMode = false;
                LastReceiptNumber = saved.InvoiceNumber;

                await SavePdfAndTryPrintAsync(saved.Id, saved.InvoiceNumber, ct);
            }
            catch
            {
                // Offline-first fallback: persist into SQLite WAL pending_sync_queue
                IsOfflineMode = true;
                await _localStore.EnqueueOfflineInvoiceAsync(command, ct);
                LastReceiptNumber = $"OFF-{idempotencyKey.ToString("N")[..6].ToUpperInvariant()}";
                LastSavedPdfPath = null;
                ToastMessage = $"Saved offline as {LastReceiptNumber}. Will sync on LAN reconnect.";
            }

            var phoneToShare = CustomerPhone;
            var totalToShare = GrandTotal;
            var itemCountToShare = CartLines.Count;
            if (!string.IsNullOrWhiteSpace(phoneToShare))
            {
                LastWhatsAppUrl = WhatsAppShareHelper.BuildInvoiceShareUrl(
                    phoneToShare,
                    _session.BusinessName,
                    LastReceiptNumber ?? "BILL",
                    totalToShare,
                    0m,
                    itemCountToShare,
                    _session.UpiVpa);
                HasWhatsAppShare = !string.IsNullOrEmpty(LastWhatsAppUrl);
            }
            else
            {
                LastWhatsAppUrl = null;
                HasWhatsAppShare = false;
            }

            CartLines.Clear();
            CashTendered = 0m;
            BillDiscountAmount = 0m;
            SelectedCustomerId = null;
            CustomerPhone = string.Empty;
            CustomerName = string.Empty;
            CustomerGstin = string.Empty;
            BillNotes = string.Empty;
            IsSplitPaymentEnabled = false;
            SplitPayments.Clear();
            CustomerSuggestions.Clear();
            IsCustomerSuggestionsOpen = false;
            RecalculateCart();
        }, showLoading: false);
    }

    private IReadOnlyList<InvoicePaymentInput> BuildPayments()
    {
        if (!IsSplitPaymentEnabled)
        {
            return GrandTotal > 0m
                ? [new InvoicePaymentInput(SelectedPaymentMode, GrandTotal)]
                : [];
        }

        var splitInputs = new List<InvoicePaymentInput>(3);
        if (SplitCashAmount > 0m)
        {
            splitInputs.Add(new InvoicePaymentInput(PaymentMode.Cash, SplitCashAmount));
        }

        if (SplitUpiAmount > 0m)
        {
            splitInputs.Add(new InvoicePaymentInput(PaymentMode.Upi, SplitUpiAmount));
        }

        if (SplitCardAmount > 0m)
        {
            splitInputs.Add(new InvoicePaymentInput(PaymentMode.Card, SplitCardAmount));
        }

        if (splitInputs.Count > 0)
        {
            var splitTotal = splitInputs.Sum(x => x.Amount);
            if (Math.Abs(splitTotal - GrandTotal) > 0.5m)
            {
                ToastMessage = $"Split payment total ₹{splitTotal:N2} must match bill total ₹{GrandTotal:N2}.";
                return [];
            }

            return splitInputs;
        }

        var validLines = SplitPayments
            .Where(x => x.Amount > 0m)
            .Select(x => new InvoicePaymentInput(x.Mode, x.Amount))
            .ToArray();

        if (validLines.Length == 0)
        {
            return [];
        }

        var total = validLines.Sum(x => x.Amount);
        if (Math.Abs(total - GrandTotal) > 0.5m)
        {
            ToastMessage = $"Split payment total ₹{total:N2} must match bill total ₹{GrandTotal:N2}.";
            return [];
        }

        return validLines;
    }

    private string? BuildInvoiceNotes()
    {
        var notes = new List<string>(2);
        if (BillDiscountAmount > 0)
        {
            notes.Add($"Special bill discount ₹{BillDiscountAmount:N2} applied");
        }

        if (!string.IsNullOrWhiteSpace(BillNotes))
        {
            notes.Add(BillNotes.Trim());
        }

        return notes.Count == 0 ? null : string.Join(" | ", notes);
    }

    private async Task SavePdfAndTryPrintAsync(Guid invoiceId, string invoiceNumber, CancellationToken ct)
    {
        var pdfResponse = await _api.GetInvoicePdfAsync(invoiceId, InvoicePageFormat.A4, ct);
        if (!pdfResponse.IsSuccessStatusCode)
        {
            ToastMessage = $"Bill {invoiceNumber} saved, but PDF generation failed.";
            return;
        }

        var pdfBytes = await pdfResponse.Content.ReadAsByteArrayAsync(ct);
        if (pdfBytes.Length == 0)
        {
            ToastMessage = $"Bill {invoiceNumber} saved, but received an empty PDF.";
            return;
        }

        Directory.CreateDirectory(PdfSaveDirectory);

        var rawFileName = GetFileNameFromHeaders(pdfResponse.Content.Headers.ContentDisposition?.FileNameStar)
            ?? GetFileNameFromHeaders(pdfResponse.Content.Headers.ContentDisposition?.FileName)
            ?? $"{invoiceNumber.Replace('/', '-')}.pdf";

        var fileName = EnsurePdfExtension(rawFileName);
        var fullPath = Path.Combine(PdfSaveDirectory, fileName);
        await File.WriteAllBytesAsync(fullPath, pdfBytes, ct);
        LastSavedPdfPath = fullPath;

        var printSucceeded = await TryPrintPdfAsync(fullPath);
        if (printSucceeded)
        {
            ToastMessage = $"Bill {invoiceNumber} saved, PDF saved and sent to printer.";
        }
        else
        {
            ToastMessage = $"Bill {invoiceNumber} saved. PDF saved at '{fullPath}'. No printer configured.";
        }
    }

    [RelayCommand]
    public async Task ReprintLastSavedPdfAsync()
    {
        if (string.IsNullOrWhiteSpace(LastSavedPdfPath) || !File.Exists(LastSavedPdfPath))
        {
            ToastMessage = "No saved PDF found to print.";
            return;
        }

        var printed = await TryPrintPdfAsync(LastSavedPdfPath);
        ToastMessage = printed
            ? $"Sent '{Path.GetFileName(LastSavedPdfPath)}' to printer."
            : $"Could not print. PDF is saved at '{LastSavedPdfPath}'.";
    }

    private static string? GetFileNameFromHeaders(string? fileNameHeader)
    {
        if (string.IsNullOrWhiteSpace(fileNameHeader))
        {
            return null;
        }

        var trimmed = fileNameHeader.Trim().Trim('"');
        return WebUtility.UrlDecode(trimmed);
    }

    private static string EnsurePdfExtension(string fileName)
    {
        if (fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return fileName;
        }

        return $"{fileName}.pdf";
    }

    private static Task<bool> TryPrintPdfAsync(string pdfPath)
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = pdfPath,
                Verb = "print",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };

            var process = System.Diagnostics.Process.Start(info);
            return Task.FromResult(process != null);
        }
        catch
        {
            return Task.FromResult(false);
        }
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

        if (IsSplitPaymentEnabled && SplitPayments.Count > 0)
        {
            SplitPayments[0].Amount = GrandTotal;
            OnPropertyChanged(nameof(TotalSplitPaymentAmount));
            OnPropertyChanged(nameof(RemainingSplitAmount));
        }

        if (IsSplitPaymentEnabled)
        {
            SplitCashAmount = GrandTotal;
        }

        OnPropertyChanged(nameof(ChangeToReturn));
        UpdateUpiQrUri();
    }

    public void UpdateUpiQrUri()
    {
        var vpa = _session.UpiVpa ?? "merchant@upi";
        var amount = GrandTotal;
        if (amount > 0m)
        {
            UpiQrUri = UpiPaymentHelper.GenerateUpiUri(vpa, _session.BusinessName, amount, LastReceiptNumber ?? "NEXT");
            IsUpiPaymentActive = SelectedPaymentMode == PaymentMode.Upi || SplitUpiAmount > 0m;
        }
        else
        {
            UpiQrUri = string.Empty;
            IsUpiPaymentActive = false;
        }
    }
}
