using BillMate.App.Services;
using BillMate.App.Views;
using BillMate.ClientCore.Services;
using BillMate.ClientCore.Theme;
using CommunityToolkit.Maui;

namespace BillMate.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit();

        var whiteLabel = LoadWhiteLabelConfig();
        var sqlitePath = Path.Combine(FileSystem.AppDataDirectory, "billmate_offline.db");

        builder.Services.AddBillMateClientCore(sqlitePath, whiteLabel);
        builder.Services.AddSingleton<INavigationService, MauiNavigationService>();

        builder.Services.AddTransient<PinLoginPage>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<PosPage>();
        builder.Services.AddTransient<InventoryPage>();
        builder.Services.AddTransient<ReportsPage>();
        builder.Services.AddTransient<RestoreWizardPage>();

        return builder.Build();
    }

    private static WhiteLabelConfig LoadWhiteLabelConfig()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "client_config.json");
        if (File.Exists(configPath))
            return WhiteLabelConfig.LoadFromJson(File.ReadAllText(configPath));

        return new WhiteLabelConfig();
    }
}
