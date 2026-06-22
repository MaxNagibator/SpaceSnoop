using System.Globalization;
using System.Windows.Data;

namespace SpaceSnoop.Wpf.Converters;

public sealed class FractionToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraction = values.Length > 0 && values[0] is double f ? f : 0.0;
        var maxWidth = values.Length > 1 && values[1] is double w ? w : 100.0;

        fraction = Math.Clamp(fraction, 0, 1);
        return fraction * maxWidth;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
