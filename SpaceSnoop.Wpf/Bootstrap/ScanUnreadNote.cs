namespace SpaceSnoop.Wpf.Bootstrap;

public static class ScanUnreadNote
{
    public const string RestartHint = "Программа закроется и откроется снова с правами администратора. "
        + "Результат сканирования и пометки на удаление не сохранятся – сканирование нужно будет запустить заново.";

    public static string? Describe(long unreadDirectories)
    {
        if (unreadDirectories <= 0)
        {
            return null;
        }

        var count = (int)Math.Min(unreadDirectories, int.MaxValue);

        return $"{Plural.Format(count, "каталог", "каталога", "каталогов")} {Plural.Word(count, "не прочитан", "не прочитано", "не прочитано")}";
    }

    public static string Explain(bool isElevated)
    {
        return isElevated
            ? "Эти каталоги не удалось открыть даже с правами администратора: доступ к ним закрыт, "
            + "они исчезли во время сканирования или путь к ним слишком длинный. "
            + "Их файлы в итог не вошли, пути записаны в журнал."
            : "Файлы этих каталогов в итог не вошли. Скорее всего, часть из них закрыта для обычного режима, "
            + "и после перезапуска с правами администратора прочитается больше. "
            + "Остальные могли исчезнуть во время сканирования или быть закрыты для всех, пути записаны в журнал.";
    }
}
