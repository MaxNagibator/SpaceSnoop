namespace SpaceSnoop.Core;

public sealed class ScanRootLinkException(string path, string message) : IOException(message)
{
    public string Path { get; } = path;

    public static string Describe(string path)
    {
        return $"Папка «{path}» – ссылка на другое место. Ссылки при сканировании не открываются, чтобы одни и те же файлы не посчитались дважды. Выберите папку, на которую она указывает.";
    }

    public static string DescribeUnverified(string path)
    {
        return $"Не удалось проверить папку «{path}»: Windows не сообщила, куда она ведёт. Такие папки при сканировании не открываются, чтобы одни и те же файлы не посчитались дважды. Проверьте доступ к папке или выберите другую.";
    }
}
