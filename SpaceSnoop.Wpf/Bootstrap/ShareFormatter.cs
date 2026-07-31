namespace SpaceSnoop.Wpf.Bootstrap;

public static class ShareFormatter
{
    public const double TinyFraction = 0.001;

    private const double NearWholePercent = 99.9;

    public static string WidestSample { get; } = Format(TinyFraction / 2);

    public static string Format(double fraction)
    {
        if (double.IsNaN(fraction) || fraction <= 0)
        {
            return "0 %";
        }

        if (fraction >= 1)
        {
            return "100 %";
        }

        if (fraction < TinyFraction)
        {
            return $"<{TinyFraction * 100:0.#} %";
        }

        return $"{Math.Min(fraction * 100, NearWholePercent):0.#} %";
    }
}
