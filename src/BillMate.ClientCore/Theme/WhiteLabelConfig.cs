using System.Text.Json;

namespace BillMate.ClientCore.Theme;

public sealed record WhiteLabelConfig(
    string BusinessName = "BillMate Store",
    string AppTitle = "BillMate Billing",
    string LogoPath = "Assets/logo.png",
    string PrimaryColor = "#1565C0",
    string AccentColor = "#FF6F00",
    string InvoicePrefix = "INV",
    string GstinDefault = "",
    string State = "Maharashtra",
    string StateCode = "27",
    string SupportPhone = "")
{
    public static WhiteLabelConfig LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new WhiteLabelConfig();
        return JsonSerializer.Deserialize<WhiteLabelConfig>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? new WhiteLabelConfig();
    }
}

/// <summary>
/// Centralized Senior-Frontend 8pt Grid & Elevated Surface Tokens (Dark & Light Parity).
/// </summary>
public static class DesignTokens
{
    public const double SpaceXS = 4;
    public const double SpaceSM = 8;
    public const double SpaceMD = 12;
    public const double SpaceLG = 16;
    public const double SpaceXL = 24;
    public const double Space2XL = 32;

    public const double RadiusCard = 16;
    public const double RadiusButton = 12;
    public const double MinTouchTarget = 48;
}
