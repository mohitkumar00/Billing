using BillMate.ClientCore.ViewModels;

namespace BillMate.App.Views;

public partial class RestoreWizardPage : ContentPage
{
    private readonly RestoreWizardViewModel _vm;

    public RestoreWizardPage(RestoreWizardViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadBackupsCommand.ExecuteAsync(null);
    }
}
