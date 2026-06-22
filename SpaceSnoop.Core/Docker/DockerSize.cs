using System.Globalization;

namespace SpaceSnoop.Core.Docker;

public static class DockerSize
{
    public static long ToBytes(string human)
    {
        if (string.IsNullOrWhiteSpace(human))
        {
            return 0;
        }

        var span = human.AsSpan().Trim();
        var split = 0;
        while (split < span.Length && (char.IsDigit(span[split]) || span[split] is '.' or ','))
        {
            split++;
        }

        var number = span[..split].ToString().Replace(',', '.');
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return 0;
        }

        var factor = span[split..].Trim().ToString().ToUpperInvariant() switch
        {
            "KB" => 1e3,
            "MB" => 1e6,
            "GB" => 1e9,
            "TB" => 1e12,
            "PB" => 1e15,
            _ => 1d,
        };

        return (long)(value * factor);
    }
}
