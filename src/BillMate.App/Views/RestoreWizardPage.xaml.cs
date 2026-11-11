using BillMate.ClientCore.ViewModels;

namespace BillMate.App.Views;

public partial class RestoreWizardPage : ContentPage
{
    public RestoreWizardPage(RestoreWizardViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
