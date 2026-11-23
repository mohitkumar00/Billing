using BillMate.ClientCore.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace BillMate.App.Views;

public partial class KhataPage : ContentPage
{
    private readonly KhataViewModel _viewModel;

    public KhataPage(KhataViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCustomersCommand.ExecuteAsync(null);
    }

    private async void OnWhatsAppReminderClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: CustomerKhataItem customer })
        {
            var url = _viewModel.GetWhatsAppReminderUrl(customer);
            if (!string.IsNullOrEmpty(url))
            {
                try
                {
                    await Microsoft.Maui.ApplicationModel.Launcher.OpenAsync(new Uri(url));
                }
                catch (Exception ex)
                {
                    await DisplayAlert("WhatsApp Launch", $"Could not open WhatsApp: {ex.Message}", "OK");
                }
            }
            else
            {
                await DisplayAlert("WhatsApp Reminder", "Customer does not have a valid mobile number.", "OK");
            }
        }
    }
}
