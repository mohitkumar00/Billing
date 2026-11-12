using Microsoft.Maui.Controls;

namespace BillMate.App.Controls;

public partial class AppNavBar : ContentView
{
    public static readonly BindableProperty ActiveTabProperty =
        BindableProperty.Create(
            nameof(ActiveTab),
            typeof(string),
            typeof(AppNavBar),
            defaultValue: "Dashboard",
            propertyChanged: (bindable, oldValue, newValue) =>
            {
                if (bindable is AppNavBar nav)
                {
                    nav.UpdateTabVisuals();
                }
            });

    public string ActiveTab
    {
        get => (string)GetValue(ActiveTabProperty);
        set => SetValue(ActiveTabProperty, value);
    }

    public AppNavBar()
    {
        InitializeComponent();
        UpdateTabVisuals();
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        UpdateTabVisuals();
    }

    private void UpdateTabVisuals()
    {
        var active = ActiveTab?.ToLowerInvariant() ?? string.Empty;

        SetButtonState(BtnDashboard, active == "dashboard");
        SetButtonState(BtnPos, active == "pos" || active == "billing");
        SetButtonState(BtnInventory, active == "inventory" || active == "stock");
        SetButtonState(BtnReports, active == "reports" || active == "tax");
        SetButtonState(BtnRestore, active == "restore" || active == "backup");

        if (BtnCashier != null)
        {
            if (active == "pinlogin" || active == "cashier")
            {
                BtnCashier.BackgroundColor = Color.FromArgb("#38BDF8");
                BtnCashier.TextColor = Color.FromArgb("#0F172A");
            }
            else
            {
                BtnCashier.BackgroundColor = Color.FromArgb("#27272A");
                BtnCashier.TextColor = Color.FromArgb("#38BDF8");
            }
        }
    }

    private static void SetButtonState(Button? btn, bool isActive)
    {
        if (btn == null) return;

        if (isActive)
        {
            btn.BackgroundColor = Color.FromArgb("#2563EB");
            btn.TextColor = Color.FromArgb("#FFFFFF");
            btn.BorderWidth = 1.5;
            btn.BorderColor = Color.FromArgb("#60A5FA");
        }
        else
        {
            btn.BackgroundColor = Color.FromArgb("#27272A");
            btn.TextColor = Color.FromArgb("#E2E8F0");
            btn.BorderWidth = 0;
            btn.BorderColor = Colors.Transparent;
        }
    }

    private void OnDashboardClicked(object? sender, EventArgs e) => NavigateToRoute("dashboard");
    private void OnPosClicked(object? sender, EventArgs e) => NavigateToRoute("pos");
    private void OnInventoryClicked(object? sender, EventArgs e) => NavigateToRoute("inventory");
    private void OnReportsClicked(object? sender, EventArgs e) => NavigateToRoute("reports");
    private void OnRestoreClicked(object? sender, EventArgs e) => NavigateToRoute("restore");
    private void OnCashierClicked(object? sender, EventArgs e) => NavigateToRoute("pinlogin");

    private static void NavigateToRoute(string route)
    {
        try
        {
            if (Shell.Current is null) return;

            var cleanKey = route.TrimStart('/').ToLowerInvariant();

            // 1. Direct ShellItem switch (Instant, synchronous on UI thread, zero deadlock risk)
            foreach (var item in Shell.Current.Items)
            {
                var itemRoute = item.Route?.ToLowerInvariant() ?? string.Empty;
                if (itemRoute == cleanKey || itemRoute.EndsWith(cleanKey))
                {
                    Shell.Current.CurrentItem = item;
                    return;
                }

                foreach (var section in item.Items)
                {
                    var secRoute = section.Route?.ToLowerInvariant() ?? string.Empty;
                    if (secRoute == cleanKey || secRoute.EndsWith(cleanKey))
                    {
                        Shell.Current.CurrentItem = item;
                        item.CurrentItem = section;
                        return;
                    }
                }
            }

            // 2. Safe async fallback if not in top items
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await Shell.Current.GoToAsync($"//{cleanKey}");
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppNavBar navigation error to {route}: {ex.Message}");
        }
    }
}
