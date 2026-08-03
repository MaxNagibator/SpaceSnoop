using System.Globalization;
using System.Windows.Data;

namespace SpaceSnoop.Wpf.Converters;

public sealed class TileColumnsConverter : IMultiValueConverter
{
    public static int Columns(double width, double fontSize, double minTileWidth, int maxColumns)
    {
        if (!double.IsFinite(width) || width <= 0 || minTileWidth <= 0 || maxColumns < 1)
        {
            return Math.Max(1, maxColumns);
        }

        var scale = double.IsFinite(fontSize) && fontSize > 0 ? fontSize / AppDefaults.ShellBaseFontSize : 1;
        var tile = minTileWidth * (1 + ((scale - 1) / 2));

        return Math.Clamp((int)(width / tile), 1, maxColumns);
    }

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var width = values.Length > 0 && values[0] is double actual ? actual : 0;
        var fontSize = values.Length > 1 && values[1] is double size ? size : AppDefaults.ShellBaseFontSize;

        return Columns(width, fontSize, AppDefaults.PerformanceTileMinWidth, AppDefaults.PerformanceTileColumnsMax);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
