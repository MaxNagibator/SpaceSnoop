using System.Windows.Media;

namespace SpaceSnoop.Wpf.Converters;

public static class HeatColor
{
    private const double HueGreen = 130.0;
    private const double Saturation = 0.62;
    private const double Value = 0.82;
    private const double ExponentBase = 8.0;

    public static Color From(double fraction, double intensity)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        intensity = Math.Clamp(intensity, AppDefaults.IntensityMin, AppDefaults.IntensityMax);

        var adjusted = Math.Pow(fraction, ExponentBase / intensity);
        var hue = HueGreen * (1 - adjusted);

        return FromHsv(hue, Saturation, Value);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var hi = (int)Math.Floor(hue / 60) % 6;
        var f = hue / 60 - Math.Floor(hue / 60);

        var v = (byte)Math.Round(value * 255);
        var p = (byte)Math.Round(value * (1 - saturation) * 255);
        var q = (byte)Math.Round(value * (1 - f * saturation) * 255);
        var t = (byte)Math.Round(value * (1 - (1 - f) * saturation) * 255);

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
}
