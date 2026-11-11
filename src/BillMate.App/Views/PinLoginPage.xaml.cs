using BillMate.ClientCore.ViewModels;

namespace BillMate.App.Views;

public partial class PinLoginPage : ContentPage
{
    public PinLoginPage(PinLoginViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
