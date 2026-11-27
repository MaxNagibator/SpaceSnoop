using System.ComponentModel;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Converters;

public readonly record struct HeatStop(double Offset, Color Color);

public static class HeatColor
{
    public const string PaletteStopsResourceKey = "DataScale.Stops";

    private const double ClayHue = 15.0;
    private const double SaturationCool = 0.14;
    private const double SaturationHot = 0.78;
    private const double ValueCool = 0.88;
    private const double ValueHot = 0.66;
    private const double ExponentBase = 8.0;

    private static readonly HeatStop[] DefaultSpectrumStops =
    [
        new(0.00, Color.FromRgb(0xE0, 0xC9, 0xC1)),
        new(0.33, Color.FromRgb(0xCE, 0x98, 0x86)),
        new(0.66, Color.FromRgb(0xBB, 0x6C, 0x52)),
        new(1.00, Color.FromRgb(0xA8, 0x46, 0x25)),
    ];

    private static readonly LinearGradientBrush DefaultHeatHorizontal = BuildSpectrum(DefaultSpectrumStops, horizontal: true);
    private static readonly LinearGradientBrush DefaultHeatVertical = BuildSpectrum(DefaultSpectrumStops, horizontal: false);

    private static bool _themeSyncAttached;

    public static HeatPaletteState Palette { get; } = new();

    public static void AttachThemeSync()
    {
        if (!_themeSyncAttached)
        {
            _themeSyncAttached = true;
            ThemeManager.Changed += OnThemeChanged;
        }

        SyncFromActiveTheme();
    }

    public static void SyncFromActiveTheme()
    {
        var stops = ExtractStops(ThemeManager.FindPaletteDictionary()?[PaletteStopsResourceKey] as GradientStopCollection);
        Palette.Update(stops);

        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        app.Resources["HeatSpectrum"] = stops is null
            ? DefaultHeatHorizontal
            : BuildSpectrum(stops, horizontal: true);
        app.Resources["HeatSpectrumVertical"] = stops is null
            ? DefaultHeatVertical
            : BuildSpectrum(stops, horizontal: false);
    }

    public static Color From(double fraction, double intensity) =>
        From(fraction, intensity, Palette.Stops);

    public static Color From(double fraction, double intensity, ReadOnlySpan<HeatStop> stops)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        intensity = Math.Clamp(intensity, AppDefaults.IntensityMin, AppDefaults.IntensityMax);

        var adjusted = Math.Pow(fraction, ExponentBase / intensity);

        if (stops.Length >= 2)
        {
            return Interpolate(stops, adjusted);
        }

        var saturation = SaturationCool + ((SaturationHot - SaturationCool) * adjusted);
        var value = ValueCool - ((ValueCool - ValueHot) * adjusted);

        return FromHsv(ClayHue, saturation, value);
    }

    private static void OnThemeChanged(object? sender, ThemeDefinition theme) =>
        SyncFromActiveTheme();

    private static HeatStop[]? ExtractStops(GradientStopCollection? collection)
    {
        if (collection is null || collection.Count < 2)
        {
            return null;
        }

        var result = new HeatStop[collection.Count];
        for (var i = 0; i < collection.Count; i++)
        {
            var stop = collection[i];
            result[i] = new HeatStop(Math.Clamp(stop.Offset, 0, 1), stop.Color);
        }

        Array.Sort(result, static (a, b) => a.Offset.CompareTo(b.Offset));
        return result;
    }

    private static LinearGradientBrush BuildSpectrum(ReadOnlySpan<HeatStop> stops, bool horizontal)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = horizontal ? new Point(1, 0) : new Point(0, 1),
        };

        if (horizontal)
        {
            for (var i = 0; i < stops.Length; i++)
            {
                brush.GradientStops.Add(new GradientStop(stops[i].Color, stops[i].Offset));
            }
        }
        else
        {
            for (var i = stops.Length - 1; i >= 0; i--)
            {
                brush.GradientStops.Add(new GradientStop(stops[i].Color, 1.0 - stops[i].Offset));
            }
        }

        brush.Freeze();
        return brush;
    }

    private static Color Interpolate(ReadOnlySpan<HeatStop> stops, double t)
    {
        if (t <= stops[0].Offset)
        {
            return stops[0].Color;
        }

        for (var i = 1; i < stops.Length; i++)
        {
            var prev = stops[i - 1];
            var next = stops[i];
            if (t <= next.Offset)
            {
                var span = next.Offset - prev.Offset;
                var local = span <= 0 ? 0 : (t - prev.Offset) / span;
                return Color.FromRgb(
                    Lerp(prev.Color.R, next.Color.R, local),
                    Lerp(prev.Color.G, next.Color.G, local),
                    Lerp(prev.Color.B, next.Color.B, local));
            }
        }

        return stops[^1].Color;
    }

    private static byte Lerp(byte a, byte b, double t) =>
        (byte)Math.Round(a + ((b - a) * t));

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var hi = (int)Math.Floor(hue / 60) % 6;
        var f = (hue / 60) - Math.Floor(hue / 60);

        var v = (byte)Math.Round(value * 255);
        var p = (byte)Math.Round(value * (1 - saturation) * 255);
        var q = (byte)Math.Round(value * (1 - (f * saturation)) * 255);
        var t = (byte)Math.Round(value * (1 - ((1 - f) * saturation)) * 255);

        return hi switch
        {
            0 => Color.FromRgb(v, t, p),
            1 => Color.FromRgb(q, v, p),
            2 => Color.FromRgb(p, v, t),
            3 => Color.FromRgb(p, q, v),
            4 => Color.FromRgb(t, p, v),
            _ => Color.FromRgb(v, p, q),
        };
    }

    public sealed class HeatPaletteState : INotifyPropertyChanged
    {
        private HeatStop[]? _stops;
        private int _revision;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool HasCustomStops => _stops is { Length: >= 2 };

        public int Revision => _revision;

        internal ReadOnlySpan<HeatStop> Stops => _stops;

        internal void Update(HeatStop[]? stops)
        {
            if (StopsEqual(_stops, stops))
            {
                return;
            }

            _stops = stops;
            _revision++;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Revision)));
        }

        private static bool StopsEqual(HeatStop[]? current, HeatStop[]? next)
        {
            if (ReferenceEquals(current, next))
            {
                return true;
            }

            if (current is null || next is null || current.Length != next.Length)
            {
                return false;
            }

            for (var i = 0; i < current.Length; i++)
            {
                if (current[i] != next[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
