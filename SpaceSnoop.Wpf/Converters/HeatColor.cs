using System.Windows.Media;

namespace SpaceSnoop.Wpf.Converters;

public static class HeatColor
{
    private const double ClayHue = 15.0;
    private const double SaturationCool = 0.14;
    private const double SaturationHot = 0.78;
    private const double ValueCool = 0.88;
    private const double ValueHot = 0.66;
    private const double ExponentBase = 8.0;

    public static Color From(double fraction, double intensity)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        intensity = Math.Clamp(intensity, AppDefaults.IntensityMin, AppDefaults.IntensityMax);

        var adjusted = Math.Pow(fraction, ExponentBase / intensity);
        var saturation = SaturationCool + ((SaturationHot - SaturationCool) * adjusted);
        var value = ValueCool - ((ValueCool - ValueHot) * adjusted);

        return FromHsv(ClayHue, saturation, value);
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
