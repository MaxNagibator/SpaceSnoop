namespace SpaceSnoop.Core;

/// <summary>
/// Русифицированный форматировщик размера файла.
/// </summary>
public static class SizeFormatter
{
    private const decimal SizeUnit = 1024m;
    private static readonly string[] SizeSuffixes = ["байт", "КБ", "МБ", "ГБ", "ТБ"];

    /// <summary>
    /// Форматирует размер в виде строки с суффиксом размера.
    /// </summary>
    /// <param name="size">Размер в байтах.</param>
    /// <param name="decimalPlaces">Количество знаков после запятой.</param>
    /// <returns>Строка с форматированным размером.</returns>
    public static string Format(long size, int decimalPlaces = 1)
    {
        if (size < 0)
        {
            return $"-{Format(-size, decimalPlaces)}";
        }

        if (size == 0)
        {
            return $"0 {SizeSuffixes[0]}";
        }

        var i = 0;
        var value = (decimal)size;

        while (value >= SizeUnit && i < SizeSuffixes.Length - 1)
        {
            value /= SizeUnit;
            i++;
        }

        value = Math.Round(value, decimalPlaces);

        return $"{value}{SizeSuffixes[i]}";
    }
}
