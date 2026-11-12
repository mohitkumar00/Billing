using BillMate.ClientCore.Services;

namespace BillMate.App.Services;

public sealed class MauiNavigationService : INavigationService
{
    public Task NavigateToAsync(string route)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (Shell.Current is null) return;

            var cleanKey = route.TrimStart('/').ToLowerInvariant();

            // 1. Direct Root Item switch (Instant, synchronous, zero route parsing recursion)
            foreach (var item in Shell.Current.Items)
            {
                var itemRoute = item.Route?.ToLowerInvariant() ?? string.Empty;
                if (itemRoute == cleanKey || itemRoute.EndsWith(cleanKey) || MatchesKey(cleanKey, item.Title ?? string.Empty))
                {
                    Shell.Current.CurrentItem = item;
                    return;
                }

                foreach (var section in item.Items)
                {
                    var secRoute = section.Route?.ToLowerInvariant() ?? string.Empty;
                    if (secRoute == cleanKey || secRoute.EndsWith(cleanKey) || MatchesKey(cleanKey, section.Title ?? string.Empty))
                    {
                        Shell.Current.CurrentItem = item;
                        item.CurrentItem = section;
                        return;
                    }
                }
            }

            // 2. Direct GoToAsync fallback
            try
            {
                _ = Shell.Current.GoToAsync($"//{cleanKey}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GoToAsync failed for //{cleanKey}: {ex.Message}");
            }
        });
    }

    private static bool MatchesKey(string key, string text)
    {
        return key switch
        {
            "pos" => text.Contains("pos") || text.Contains("bill"),
            "inventory" => text.Contains("inventory") || text.Contains("stock"),
            "reports" => text.Contains("report") || text.Contains("tax"),
            "dashboard" => text.Contains("dashboard"),
            "restore" => text.Contains("restore") || text.Contains("backup"),
            "pinlogin" => text.Contains("cashier") || text.Contains("pin"),
            _ => false
        };
    }
}
