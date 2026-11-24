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

    private async void OnDownloadTemplateClicked(object? sender, EventArgs e)
    {
        try
        {
            var csv = BillMate.ClientCore.Services.ExcelImportService.GetSampleCsvTemplate();
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloads))
                downloads = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var filePath = Path.Combine(downloads, "billmate_items_template.csv");
            await File.WriteAllTextAsync(filePath, csv);
            await DisplayAlert("Sample Template Saved", $"Sample template saved to:\n{filePath}\n\nYou can open this file in Excel, fill in your products, and click 'Import Excel/CSV' to upload.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not save template: {ex.Message}", "OK");
        }
    }

    private async void OnImportExcelClicked(object? sender, EventArgs e)
    {
        try
        {
            var options = new PickOptions
            {
                PickerTitle = "Select Excel (.xlsx) or CSV (.csv) file",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, [".xlsx", ".csv", ".txt"] },
                    { DevicePlatform.Android, ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "text/csv", "text/plain", "*/*"] }
                })
            };

            var fileResult = await FilePicker.Default.PickAsync(options);
            if (fileResult is null) return;

            using var stream = await fileResult.OpenReadAsync();
            var items = await BillMate.ClientCore.Services.ExcelImportService.ParseStreamAsync(stream, fileResult.FileName);

            if (items.Count == 0)
            {
                await DisplayAlert("Import Failed", "No valid item rows were found in the selected file.", "OK");
                return;
            }

            var confirm = await DisplayAlert("Confirm Import", $"Found {items.Count} items in '{fileResult.FileName}'.\n\nDo you want to import them into inventory?", "Import Now", "Cancel");
            if (!confirm) return;

            await _vm.ImportProductsCommand.ExecuteAsync(items);
            await DisplayAlert("Import Complete", _vm.ToastMessage ?? $"Processed {items.Count} items.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Import Error", $"Error reading Excel/CSV file: {ex.Message}", "OK");
        }
    }
}
