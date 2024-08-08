namespace SpaceSnoop.Core;

/// <summary>
///     Калькулятор для вычисления цвета на основе размера файла или директории.
/// </summary>
public class SpaceColorCalculator
{
    /// <summary>
    ///     Максимальное значение компонента цвета.
    /// </summary>
    private const int MaxComponentValue = 255;

    /// <summary>
    ///     Минимальная интенсивность цвета.
    /// </summary>
    public const int MinIntensity = 0;

    /// <summary>
    ///     Максимальная интенсивность цвета.
    /// </summary>
    public const int MaxIntensity = 100;

    /// <summary>
    ///     Значение интенсивности цвета по умолчанию.
    /// </summary>
    public const int DefaultIntensity = 10;

    /// <summary>
    ///     Текущая интенсивность цвета (насколько выражен цвет в зависимости от размера).
    /// </summary>
    private int _intensity = DefaultIntensity;

    /// <inheritdoc cref="_intensity" />
    public int Intensity
    {
        get => _intensity;
        set
        {
            if (value is < MinIntensity or > MaxIntensity)
            {
                return;
            }

            _intensity = value;
        }
    }

    /// <summary>
    ///     Получает цвет на основе размера директории.
    /// </summary>
    /// <param name="directory">Директория, для которой нужно получить цвет.</param>
    /// <param name="maxSize">Максимальный размер директории.</param>
    /// <returns>Цвет, соответствующий размеру директории.</returns>
    public Color GetColorBasedOnSize(DirectorySpace directory, long maxSize)
    {
        return GetColor(directory.TotalSize, maxSize);
    }

    /// <summary>
    ///     Получает цвет на основе размера файла.
    /// </summary>
    /// <param name="file">Файл, для которого нужно получить цвет.</param>
    /// <param name="maxSize">Максимальный размер файла.</param>
    /// <returns>Цвет, соответствующий размеру файла.</returns>
    public Color GetColorBasedOnSize(FileSpace file, long maxSize)
    {
        return GetColor(file.Size, maxSize);
    }

    /// <summary>
    ///     Вычисляет цвет на основе размера.
    /// </summary>
    /// <param name="size">Размер файла или директории.</param>
    /// <param name="maxSize">Максимальный размер файла или директории.</param>
    /// <returns>Цвет, соответствующий размеру.</returns>
    private Color GetColor(long size, long maxSize)
    {
        int red = 0;

        if (size > 0 && maxSize > 0 && maxSize >= size)
        {
            red = (int)(Map(size, 0, maxSize, 0, MaxComponentValue) * (Intensity / 10d));
            red = Constrain(red, 0, MaxComponentValue);
        }

        return Color.FromArgb(red, 0, 0);
    }

    /// <summary>
    ///     Линейно отображает значение из одного диапазона в другой.
    /// </summary>
    /// <param name="x">Значение, которое нужно отобразить.</param>
    /// <param name="inMin">Минимальное значение входного диапазона.</param>
    /// <param name="inMax">Максимальное значение входного диапазона.</param>
    /// <param name="outMin">Минимальное значение выходного диапазона.</param>
    /// <param name="outMax">Максимальное значение выходного диапазона.</param>
    /// <returns>Отображенное значение.</returns>
    /// <remarks>
    ///     <see href="https://www.arduino.cc/reference/en/language/functions/math/map/">Источник</see>
    /// </remarks>
    private static long Map(long x, long inMin, long inMax, long outMin, long outMax)
    {
        return (x - inMin) * (outMax - outMin) / (inMax - inMin) + outMin;
    }

    /// <summary>
    ///     Ограничивает значение в заданном диапазоне.
    /// </summary>
    /// <param name="x">Значение, которое нужно ограничить.</param>
    /// <param name="min">Минимальное значение диапазона.</param>
    /// <param name="max">Максимальное значение диапазона.</param>
    /// <returns>Ограниченное значение.</returns>
    /// <remarks>
    ///     <see href="https://www.arduino.cc/reference/en/language/functions/math/constrain/">Источник</see>
    /// </remarks>
    private static int Constrain(int x, int min, int max)
    {
        if (x < min)
        {
            return min;
        }

        if (x > max)
        {
            return max;
        }

        return x;
    }
}
