using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Converters;

public sealed class HeatBrushConverter : IMultiValueConverter
{
    private const int QuantizedSteps = 256;

    private static readonly Lock Sync = new();
    private static PaletteCache _cache = new(HeatColor.Palette.Revision);

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraction = values.Length > 0 && values[0] is double f ? f : 0;
        var intensity = values.Length > 1 && values[1] is double i ? i : AppDefaults.IntensityDefault;

        return GetBrush(fraction, intensity);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    internal static SolidColorBrush GetBrush(double fraction, double intensity, bool dimmed = false)
    {
        var cache = GetCurrentCache();
        var bucket = QuantizeBucket(fraction, intensity);
        var table = dimmed ? cache.Dimmed : cache.Opaque;
        var brush = Volatile.Read(ref table[bucket]);

        if (brush is not null)
        {
            return brush;
        }

        var color = HeatColor.From((double)bucket / (QuantizedSteps - 1), AppDefaults.IntensityDefault);
        var created = new SolidColorBrush(color);

        if (dimmed)
        {
            created.Opacity = 0.5;
        }

        created.Freeze();
        return Interlocked.CompareExchange(ref table[bucket], created, null) ?? created;
    }

    private static PaletteCache GetCurrentCache()
    {
        var revision = HeatColor.Palette.Revision;
        var cache = Volatile.Read(ref _cache);

        if (cache.Revision == revision)
        {
            return cache;
        }

        lock (Sync)
        {
            if (_cache.Revision != revision)
            {
                _cache = new(revision);
            }

            return _cache;
        }
    }

    private static int QuantizeBucket(double fraction, double intensity)
    {
        if (double.IsNaN(fraction))
        {
            fraction = 0;
        }

        if (double.IsNaN(intensity))
        {
            intensity = AppDefaults.IntensityDefault;
        }

        var clampedFraction = Math.Clamp(fraction, 0, 1);
        var clampedIntensity = Math.Clamp(intensity, AppDefaults.IntensityMin, AppDefaults.IntensityMax);
        var adjusted = Math.Pow(clampedFraction, AppDefaults.IntensityDefault / clampedIntensity);

        return Math.Clamp((int)Math.Round(adjusted * (QuantizedSteps - 1)), 0, QuantizedSteps - 1);
    }

    private sealed class PaletteCache(int revision)
    {
        public readonly int Revision = revision;
        public readonly SolidColorBrush?[] Opaque = new SolidColorBrush?[QuantizedSteps];
        public readonly SolidColorBrush?[] Dimmed = new SolidColorBrush?[QuantizedSteps];
    }
}
