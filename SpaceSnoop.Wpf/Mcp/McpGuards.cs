using ModelContextProtocol;
using SpaceSnoop.Core.Export;
using System.IO;
using System.Security;

namespace SpaceSnoop.Wpf.Mcp;

internal static class McpGuards
{
    public static int ClampEntryLimit(int entryLimit)
    {
        return Math.Clamp(entryLimit, AppDefaults.McpEntryLimitMin, AppDefaults.McpEntryLimitMax);
    }

    public static int ClampDepth(int depth)
    {
        return Math.Clamp(depth, ScanExport.MinDepth, ScanExport.MaxDepth);
    }

    public static int ClampHistoryPoints(int points)
    {
        return Math.Clamp(points, AppDefaults.PerformanceHistoryPointsMin, AppDefaults.PerformanceHistoryPointsMax);
    }

    public static int ClampHistorySeconds(int seconds)
    {
        return Math.Clamp(seconds, 0, AppDefaults.PerformanceHistorySecondsMax);
    }

    public static string ValidateScanPath(string path)
    {
        if (path.Length == 0)
        {
            throw new McpException("Путь к каталогу должен быть задан.");
        }

        string full;

        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            throw new McpException($"Путь «{path}» не удалось разобрать: {exception.Message}");
        }

        if (!Directory.Exists(full))
        {
            throw new McpException($"Каталог «{path}» не найден или недоступен.");
        }

        return full;
    }

    public static void Validate(string left, string right, SyncMode mode)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            throw new McpException("Оба каталога должны быть заданы.");
        }

        if (SyncProfile.PathsOverlap(left, right))
        {
            throw new McpException("Каталоги совпадают или вложены друг в друга.");
        }

        if (SyncProfile.SourceMissing(left, right, mode))
        {
            throw new McpException("Каталог-источник недоступен.");
        }
    }

    public static void RequireMutations(McpPreferences preferences, ILogger logger, string tool)
    {
        if (preferences.AllowMutations)
        {
            return;
        }

        logger.McpToolRejected(tool, "изменяющие операции запрещены");
        throw new McpException("Изменяющие операции запрещены. Включите «Разрешить изменяющие операции» в настройках приложения.");
    }
}
