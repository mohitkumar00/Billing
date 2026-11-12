using BillMate.App.Services;

namespace BillMate.App;

public partial class App : Microsoft.Maui.Controls.Application
{
    public App()
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Dark;

        try
        {
            Task.Run(async () =>
            {
                await BackendSupervisor.EnsureBackendRunningAsync();
            });
        }
        catch { }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => BackendSupervisor.Shutdown();
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(new AppShell());
}
