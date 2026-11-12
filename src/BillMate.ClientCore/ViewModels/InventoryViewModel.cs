using System.Collections.ObjectModel;
using BillMate.Application.Features.Products;
using BillMate.ClientCore.Api;
using BillMate.ClientCore.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BillMate.ClientCore.ViewModels;

public sealed partial class InventoryViewModel : StatefulViewModelBase
{
    private readonly IBillMateApi _api;
    private readonly INavigationService? _navigation;

    // Default GST Tax Slabs (seeded in database)
    public static readonly (string Name, decimal Rate, Guid Id)[] AvailableTaxSlabs =
    [
        ("GST 0% (Exempt)", 0m, Guid.Parse("11c47336-26dd-4fc7-a3db-795b35c380a2")),
        ("GST 5%", 5m, Guid.Parse("9f246e81-a1a2-4a80-a2af-ea3d922b27a3")),
        ("GST 12%", 12m, Guid.Parse("0af1a280-1d2b-414c-b09c-fd27504ea22f")),
        ("GST 18%", 18m, Guid.Parse("de966963-150e-4888-9449-6e2cddfb88bc")),
        ("GST 28%", 28m, Guid.Parse("5878dcdf-1d82-4ca0-a668-26ff5f5b2437"))
    ];

    public ObservableCollection<ProductDto> Products { get; } = [];
    public ObservableCollection<ProductDto> FilteredProducts { get; } = [];

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _selectedFilterIndex = 0; // 0 = All, 1 = Low Stock, 2 = Out of Stock

    [ObservableProperty]
    private ProductDto? _selectedProduct;

    [ObservableProperty]
    private bool _isAdjustDialogOpen;

    [ObservableProperty]
    private decimal _stockDelta = 10m;

    [ObservableProperty]
    private string _stockAdjustmentReason = "Supplier Delivery / Restock";

    [ObservableProperty]
    private bool _isNewProductDialogOpen;

    // New Product form fields
    [ObservableProperty]
    private string _newProductName = string.Empty;

    [ObservableProperty]
    private string _newProductSku = string.Empty;

    [ObservableProperty]
    private string _newProductBarcode = string.Empty;

    [ObservableProperty]
    private decimal _newProductSellingPrice;

    [ObservableProperty]
    private decimal _newProductPurchasePrice;

    [ObservableProperty]
    private decimal _newProductMrp;

    [ObservableProperty]
    private decimal _newProductOpeningStock;

    [ObservableProperty]
    private decimal _newProductLowStockThreshold = 10m;

    [ObservableProperty]
    private string _newProductUnit = "PCS";

    [ObservableProperty]
    private string _newProductImagePath = string.Empty;

    [ObservableProperty]
    private bool _hasSelectedImage;

    [ObservableProperty]
    private int _selectedTaxSlabIndex = 3; // Defaults to GST 18%

    public InventoryViewModel(IBillMateApi api, INavigationService? navigation = null)
    {
        _api = api;
        _navigation = navigation;
    }

    [RelayCommand]
    public async Task NavigateToPosAsync()
    {
        if (_navigation != null)
        {
            await _navigation.NavigateToAsync("//pos");
        }
    }

    [RelayCommand]
    public async Task NavigateToDashboardAsync()
    {
        if (_navigation != null)
        {
            await _navigation.NavigateToAsync("//dashboard");
        }
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedFilterIndexChanged(int value) => ApplyFilter();

    [RelayCommand]
    public async Task LoadInventoryAsync(CancellationToken ct = default)
    {
        await SafeExecuteAsync(async () =>
        {
            var list = await _api.SearchProductsAsync(null, 200, ct);
            Products.Clear();
            foreach (var p in list) Products.Add(p);
            ApplyFilter();
            IsEmpty = Products.Count == 0;
        });
    }

    public void ApplyFilter()
    {
        FilteredProducts.Clear();
        var q = SearchQuery.Trim();

        IEnumerable<ProductDto> query = Products;

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(p =>
                p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Sku.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (p.Barcode != null && p.Barcode.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        query = SelectedFilterIndex switch
        {
            1 => query.Where(p => !p.IsService && p.CurrentStock <= p.LowStockThreshold && p.CurrentStock > 0),
            2 => query.Where(p => !p.IsService && p.CurrentStock <= 0),
            _ => query
        };

        foreach (var item in query) FilteredProducts.Add(item);
    }

    [RelayCommand]
    public void OpenAdjustStock(ProductDto product)
    {
        SelectedProduct = product;
        StockDelta = 10m;
        StockAdjustmentReason = "Supplier Delivery / Restock";
        IsAdjustDialogOpen = true;
    }

    [RelayCommand]
    public void CloseAdjustStock()
    {
        IsAdjustDialogOpen = false;
        SelectedProduct = null;
    }

    [RelayCommand]
    public async Task SubmitAdjustStockAsync(CancellationToken ct = default)
    {
        if (SelectedProduct == null || StockDelta == 0) return;

        await SafeExecuteAsync(async () =>
        {
            var updated = await _api.AdjustStockAsync(
                SelectedProduct.Id,
                new StockAdjustBody(StockDelta, StockAdjustmentReason.Trim()),
                ct);

            ToastMessage = $"Stock for '{updated.Name}' updated to {updated.CurrentStock:0.#} {updated.Unit}.";
            IsAdjustDialogOpen = false;
            SelectedProduct = null;

            await LoadInventoryAsync(ct);
        });
    }

    [RelayCommand]
    public async Task QuickAddStockAsync(ProductDto product, CancellationToken ct = default)
    {
        await SafeExecuteAsync(async () =>
        {
            var updated = await _api.AdjustStockAsync(
                product.Id,
                new StockAdjustBody(10m, "Quick Counter Restock (+10)"),
                ct);

            ToastMessage = $"+10 added to '{updated.Name}'. New stock: {updated.CurrentStock:0.#}";
            await LoadInventoryAsync(ct);
        });
    }

    [RelayCommand]
    public async Task QuickDeductStockAsync(ProductDto product, CancellationToken ct = default)
    {
        if (product.CurrentStock <= 0) return;

        await SafeExecuteAsync(async () =>
        {
            var updated = await _api.AdjustStockAsync(
                product.Id,
                new StockAdjustBody(-1m, "Damaged / Discarded (-1)"),
                ct);

            ToastMessage = $"-1 deducted from '{updated.Name}'. Remaining: {updated.CurrentStock:0.#}";
            await LoadInventoryAsync(ct);
        });
    }

    [RelayCommand]
    public void OpenNewProductDialog()
    {
        NewProductName = string.Empty;
        NewProductSku = $"SKU-{DateTime.Now:MMddHHmm}";
        NewProductBarcode = string.Empty;
        NewProductSellingPrice = 0m;
        NewProductPurchasePrice = 0m;
        NewProductMrp = 0m;
        NewProductOpeningStock = 10m;
        NewProductLowStockThreshold = 5m;
        NewProductUnit = "PCS";
        NewProductImagePath = string.Empty;
        HasSelectedImage = false;
        SelectedTaxSlabIndex = 3; // GST 18%
        IsNewProductDialogOpen = true;
    }

    [RelayCommand]
    public void SetProductImage(string path)
    {
        NewProductImagePath = path;
        HasSelectedImage = !string.IsNullOrWhiteSpace(path);
    }

    [RelayCommand]
    public void ClearProductImage()
    {
        NewProductImagePath = string.Empty;
        HasSelectedImage = false;
    }

    [RelayCommand]
    public void CloseNewProductDialog()
    {
        IsNewProductDialogOpen = false;
    }

    [RelayCommand]
    public async Task SubmitNewProductAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(NewProductName) || string.IsNullOrWhiteSpace(NewProductSku))
        {
            ToastMessage = "Product Name and SKU are required.";
            return;
        }

        var slab = AvailableTaxSlabs[Math.Clamp(SelectedTaxSlabIndex, 0, AvailableTaxSlabs.Length - 1)];

        await SafeExecuteAsync(async () =>
        {
            var cmd = new CreateProductCommand(
                TaxSlabId: slab.Id,
                Sku: NewProductSku.Trim().ToUpperInvariant(),
                Name: NewProductName.Trim(),
                SellingPrice: NewProductSellingPrice,
                PurchasePrice: NewProductPurchasePrice,
                Mrp: NewProductMrp > 0 ? NewProductMrp : NewProductSellingPrice,
                OpeningStock: NewProductOpeningStock,
                LowStockThreshold: NewProductLowStockThreshold,
                Unit: NewProductUnit.Trim().ToUpperInvariant(),
                Barcode: string.IsNullOrWhiteSpace(NewProductBarcode) ? null : NewProductBarcode.Trim(),
                IsService: false,
                IsTaxInclusive: true,
                ImagePath: string.IsNullOrWhiteSpace(NewProductImagePath) ? null : NewProductImagePath.Trim());

            var created = await _api.CreateProductAsync(cmd, ct);
            ToastMessage = $"Product '{created.Name}' created successfully with {created.CurrentStock:0.#} {created.Unit} stock!";
            IsNewProductDialogOpen = false;

            await LoadInventoryAsync(ct);
        });
    }
}
