using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Converters;

public sealed class HeatBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraction = values.Length > 0 && values[0] is double f ? f : 0;
        var intensity = values.Length > 1 && values[1] is double i ? i : AppDefaults.IntensityDefault;

        var brush = new SolidColorBrush(HeatColor.From(fraction, intensity));
        brush.Freeze();
        return brush;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
