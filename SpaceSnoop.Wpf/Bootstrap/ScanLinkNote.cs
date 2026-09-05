namespace SpaceSnoop.Wpf.Bootstrap;

public static class ScanLinkNote
{
    public const string Hint = "Столько байт лежит на диске под несколькими именами сразу – это жёсткие ссылки, "
        + "почти всё их количество даёт WinSxS. Обход каталогами встречает такой файл под каждым именем и считает "
        + "его заново, завышая итог на эту величину; чтение $MFT видит одну запись и считает байты один раз.";

    public static string Explain(string note)
    {
        return $"Не посчитано повторно {note} – столько занимают файлы, у которых больше одного имени. {Hint}";
    }

    public static string? Describe(long extraNameBytes)
    {
        return extraNameBytes > 0 ? $"≈{SizeFormatter.Format(extraNameBytes)}" : null;
    }
}
