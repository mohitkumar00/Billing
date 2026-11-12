using BillMate.ClientCore.ViewModels;

namespace BillMate.App.Views;

public partial class PinLoginPage : ContentPage
{
    private readonly PinLoginViewModel _vm;
    private static readonly Guid RameshCashierId = Guid.Parse("4201f643-de19-4c17-bcd8-637a1919401f");
    private static readonly Guid AdminOwnerId = Guid.Parse("4201f643-de19-4c17-bcd8-637a1919401e");

    public PinLoginPage(PinLoginViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        // Default to Ramesh Cashier
        _vm.SelectedUserId = RameshCashierId;
    }

    private void OnSelectCashierClicked(object? sender, EventArgs e)
    {
        _vm.SelectedUserId = RameshCashierId;
        _vm.ToastMessage = "Selected cashier: Ramesh Cashier";
    }

    private void OnSelectAdminClicked(object? sender, EventArgs e)
    {
        _vm.SelectedUserId = AdminOwnerId;
        _vm.ToastMessage = "Selected user: Shop Administrator";
    }
}
