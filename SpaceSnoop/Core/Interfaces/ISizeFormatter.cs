namespace SpaceSnoop.Core.Interfaces;

public interface ISizeFormatter
{
    /// <summary>
    ///     Форматирует размер в виде строки с суффиксом размера.
    /// </summary>
    /// <param name="size">Размер в байтах.</param>
    /// <param name="decimalPlaces">Количество знаков после запятой.</param>
    /// <returns>Строка с форматированным размером.</returns>
    string Format(long size, int decimalPlaces = 1);
}
