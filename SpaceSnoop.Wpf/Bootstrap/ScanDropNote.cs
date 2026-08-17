namespace SpaceSnoop.Wpf.Bootstrap;

public static class ScanDropNote
{
    public const string Hint = "Чтение $MFT разбирает разметку тома напрямую, и часть записей в дерево не попадает: "
        + "запись повреждена, её родительский каталог недоступен либо ссылка на него устарела. "
        + "Итог скана на эти объекты занижен, поэтому они названы числом, а не пропущены молча.";

    public static string? Describe(long droppedObjects, long unknownSizeFiles)
    {
        var total = droppedObjects + unknownSizeFiles;

        return total > 0 ? Count(total, "объект", "объекта", "объектов") : null;
    }

    public static string Explain(long droppedObjects, long droppedBytes, long unknownSizeFiles)
    {
        var parts = new List<string>(2);

        if (droppedObjects > 0)
        {
            parts.Add($"нет в дереве: {Count(droppedObjects, "объект", "объекта", "объектов")}"
                      + (droppedBytes > 0 ? $" на ≈{SizeFormatter.Format(droppedBytes)}" : string.Empty));
        }

        if (unknownSizeFiles > 0)
        {
            parts.Add($"размер не прочитан у {Count(unknownSizeFiles, "файла", "файлов", "файлов")}, они учтены нулём");
        }

        return $"{string.Join("; ", parts)}. {Hint}";
    }

    private static string Count(long value, string one, string few, string many)
    {
        return $"{value:N0} {Plural.Word(value, one, few, many)}";
    }
}
