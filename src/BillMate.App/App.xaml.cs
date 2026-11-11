namespace BillMate.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Dark; // Low-fatigue dark mode default for counter shifts
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(new AppShell());
}
