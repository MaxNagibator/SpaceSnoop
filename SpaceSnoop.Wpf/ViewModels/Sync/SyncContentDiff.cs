using SpaceSnoop.Wpf.Diff;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

internal static class SyncContentDiff
{
    private const long MaxDiffBytes = 5 * 1024 * 1024;

    internal static FileDiffResult Build(string leftPath, string rightPath)
    {
        var leftInfo = new FileInfo(leftPath);
        var rightInfo = new FileInfo(rightPath);

        if (!leftInfo.Exists && !rightInfo.Exists)
        {
            throw new InvalidOperationException($"Файл не найден ни с одной стороны: {leftPath}");
        }

        if (DiffUnavailable(leftInfo, out var reason) || DiffUnavailable(rightInfo, out reason))
        {
            return FileDiffResult.Unreadable(reason);
        }

        var lines = TextDiff.Compute(ReadLines(leftInfo), ReadLines(rightInfo));
        var added = lines.Count(static l => l.Kind == DiffLineKind.Added);
        var removed = lines.Count(static l => l.Kind == DiffLineKind.Removed);
        return new(lines, added, removed, null);

        static string[] ReadLines(FileInfo info)
        {
            return info.Exists ? File.ReadAllLines(info.FullName) : [];
        }
    }

    // TODO: бинарь определяем по NUL-байту; кодировку доверяем File.ReadAllLines (BOM → UTF-8)
    private static bool DiffUnavailable(FileInfo info, out string reason)
    {
        if (!info.Exists)
        {
            reason = string.Empty;
            return false;
        }

        if (info.Length > MaxDiffBytes)
        {
            reason = "Файл велик для построчного сравнения (> 5 МБ) – показано только сводное различие.";
            return true;
        }

        if (Array.IndexOf(File.ReadAllBytes(info.FullName), (byte)0) >= 0)
        {
            reason = "Файл выглядит двоичным – построчное сравнение недоступно, показано сводное различие.";
            return true;
        }

        reason = string.Empty;
        return false;
    }
}

internal sealed record FileDiffResult(IReadOnlyList<DiffLine> Lines, int Added, int Removed, string? Unavailable)
{
    public static FileDiffResult Unreadable(string reason)
    {
        return new([], 0, 0, reason);
    }
}
