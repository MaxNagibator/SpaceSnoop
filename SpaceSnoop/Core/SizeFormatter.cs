namespace SpaceSnoop.Core;

/// <summary>
///     Русифицированный форматировщик размера файла.
/// </summary>
public class SizeFormatter : ISizeFormatter
{
    private const decimal SizeUnit = 1024m;
    private readonly string[] _sizeSuffixes = ["байт", "КБ", "МБ", "ГБ", "ТБ"];

    /// <inheritdoc />
    public string Format(long size, int decimalPlaces = 1)
    {
        if (size < 0)
        {
            return $"-{Format(-size, decimalPlaces)}";
        }

        if (size == 0)
        {
            return $"0 {_sizeSuffixes[0]}";
        }

        int i = 0;
        decimal value = size;

        while (value >= SizeUnit && i < _sizeSuffixes.Length - 1)
        {
            value /= SizeUnit;
            i++;
        }

        value = Math.Round(value, decimalPlaces);

        return $"{value}{_sizeSuffixes[i]}";
    }
}
