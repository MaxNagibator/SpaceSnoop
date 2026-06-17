using System.Globalization;
using System.Windows.Data;

namespace SpaceSnoop.Wpf.Converters;

public sealed class MinTileSizeToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var width = values.Length > 0 && values[0] is double w ? w : 0.0;
        var height = values.Length > 1 && values[1] is double h ? h : 0.0;

        var minWidth = 46.0;
        var minHeight = 32.0;

        if (parameter is string spec)
        {
            var parts = spec.Split(',');

            if (parts.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var pw)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var ph))
            {
                minWidth = pw;
                minHeight = ph;
            }
        }

        return width >= minWidth && height >= minHeight ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
