namespace SpaceSnoop.Wpf.ViewModels.Chat;

public static class ChatQuestion
{
    public static string ForScanNode(string path, string sizeText, bool isDirectory)
    {
        var size = sizeText.Length > 0 ? $" ({sizeText})" : string.Empty;

        return isDirectory
            ? $"Что занимает место в `{path}`{size} и что оттуда можно убрать?"
            : $"Что это за файл `{path}`{size} и можно ли его убрать?";
    }

    public static string ForSyncNode(
        string relativePath,
        ComparisonStatus status,
        bool isDirectory,
        string leftSizeText,
        string rightSizeText,
        string diffReason)
    {
        var kind = isDirectory ? "Каталог" : "Файл";
        var details = DescribeSides(leftSizeText, rightSizeText, diffReason);

        return $"{kind} `{relativePath}` в открытом сравнении числится как «{Describe(status)}»{details}. Почему так и что с ним делать?";
    }

    public static string Describe(ComparisonStatus status)
    {
        return status switch
        {
            ComparisonStatus.LeftOnly => "только слева",
            ComparisonStatus.RightOnly => "только справа",
            ComparisonStatus.Modified => "изменён",
            ComparisonStatus.Conflict => "спорный",
            _ => "идентичен",
        };
    }

    private static string DescribeSides(string leftSizeText, string rightSizeText, string diffReason)
    {
        List<string> parts = [];

        if (leftSizeText.Length > 0)
        {
            parts.Add($"слева {leftSizeText}");
        }

        if (rightSizeText.Length > 0)
        {
            parts.Add($"справа {rightSizeText}");
        }

        if (diffReason.Length > 0)
        {
            parts.Add($"различие: {diffReason}");
        }

        return parts.Count == 0 ? string.Empty : $" ({string.Join(", ", parts)})";
    }
}
