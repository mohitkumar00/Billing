using System.Globalization;

namespace BillMate.App.Converters;

public class StockBadgeBgConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        decimal stock = 0m;
        if (value is decimal d) stock = d;
        else if (value is double db) stock = (decimal)db;
        else if (value is int i) stock = i;
        else if (value != null && decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) stock = parsed;

        var color = stock <= 0 ? Color.FromArgb("#450A0A") :
                    stock <= 10 ? Color.FromArgb("#451A03") :
                    Color.FromArgb("#064E3B");

        if (targetType == typeof(Brush))
            return new SolidColorBrush(color);

        return color;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class StockBadgeTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        decimal stock = 0m;
        if (value is decimal d) stock = d;
        else if (value is double db) stock = (decimal)db;
        else if (value is int i) stock = i;
        else if (value != null && decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) stock = parsed;

        var color = stock <= 0 ? Color.FromArgb("#FCA5A5") :
                    stock <= 10 ? Color.FromArgb("#FCD34D") :
                    Color.FromArgb("#6EE7B7");

        if (targetType == typeof(Brush))
            return new SolidColorBrush(color);

        return color;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
