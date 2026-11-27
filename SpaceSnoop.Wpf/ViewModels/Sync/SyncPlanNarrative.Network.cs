using System.IO;
using System.Security;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

internal static partial class SyncPlanNarrative
{
    internal const string NetworkDeleteWarning = "На сетевом хранилище нет корзины Windows – удаляемые и заменяемые файлы нельзя вернуть из корзины";

    private static readonly Func<string, bool> DefaultSupportsRecycleBin = RecycleBin.SupportsRecycleBin;

    internal static Func<string, bool> SupportsRecycleBin { get; set; } = DefaultSupportsRecycleBin;

    internal static bool HasUncDeleteOrOverwrite(ComparisonResult result, bool recycleOverwritten)
    {
        var leftUnc = UncPath.IsUnc(result.LeftPath);
        var rightUnc = UncPath.IsUnc(result.RightPath);

        if (!leftUnc && !rightUnc)
        {
            return false;
        }

        return HasUncAction(result.Root, leftUnc, rightUnc, recycleOverwritten);
    }

    internal static bool HasNetworkDeleteOrOverwrite(ComparisonResult result, bool recycleOverwritten)
    {
        var probe = SupportsRecycleBin;
        var isDefaultProbe = ReferenceEquals(probe, DefaultSupportsRecycleBin);
        var cache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var leftNoBin = !SupportsPath(result.LeftPath, cache, probe);
        var rightNoBin = !SupportsPath(result.RightPath, cache, probe);

        return HasNetworkAction(result.Root, result.LeftPath, result.RightPath, leftNoBin, rightNoBin, recycleOverwritten, cache, probe, isDefaultProbe);
    }

    internal static bool HasUnblockedNetworkDelete(ComparisonResult result)
    {
        var probe = SupportsRecycleBin;
        var isDefaultProbe = ReferenceEquals(probe, DefaultSupportsRecycleBin);
        var cache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var leftNoBin = !SupportsPath(result.LeftPath, cache, probe);
        var rightNoBin = !SupportsPath(result.RightPath, cache, probe);

        return SyncEngine.FindPermanentNetworkDeletePath(
            result,
            path =>
            {
                if (leftNoBin && IsUnderBase(path, result.LeftPath))
                {
                    return false;
                }

                if (rightNoBin && IsUnderBase(path, result.RightPath))
                {
                    return false;
                }

                if (cache.TryGetValue(path, out var cached))
                {
                    return cached;
                }

                var parent = Path.GetDirectoryName(path);

                if (!string.IsNullOrEmpty(parent) && !SupportsPath(parent, cache, probe))
                {
                    cache[path] = false;
                    return false;
                }

                if (!isDefaultProbe || string.IsNullOrEmpty(parent))
                {
                    var supported = probe(path);
                    cache[path] = supported;
                    return supported;
                }

                var fileSupported = !IsReparsePoint(path) || probe(path);
                cache[path] = fileSupported;
                return fileSupported;
            }) is not null;
    }

    internal static bool TargetsWithoutRecycleBin(
        string leftPath,
        string rightPath,
        SyncMode mode,
        bool mirror,
        bool recycleOverwritten)
    {
        if (!mirror && !recycleOverwritten)
        {
            return false;
        }

        return mode switch
        {
            SyncMode.LeftToRight => !string.IsNullOrWhiteSpace(rightPath) && !SupportsRecycleBin(rightPath),
            SyncMode.RightToLeft => !string.IsNullOrWhiteSpace(leftPath) && !SupportsRecycleBin(leftPath),
            _ => (!string.IsNullOrWhiteSpace(leftPath) && !SupportsRecycleBin(leftPath))
                || (!string.IsNullOrWhiteSpace(rightPath) && !SupportsRecycleBin(rightPath)),
        };
    }

    private static bool HasUncAction(DirectoryComparison dir, bool leftUnc, bool rightUnc, bool recycleOverwritten)
    {
        foreach (var file in dir.Files)
        {
            if (leftUnc && (file.Action == SyncAction.DeleteLeft || (recycleOverwritten && file.Action == SyncAction.CopyToLeft && file.Status == ComparisonStatus.Modified)))
            {
                return true;
            }

            if (rightUnc && (file.Action == SyncAction.DeleteRight || (recycleOverwritten && file.Action == SyncAction.CopyToRight && file.Status == ComparisonStatus.Modified)))
            {
                return true;
            }
        }

        foreach (var sub in dir.SubDirectories)
        {
            if ((leftUnc && sub.Action == SyncAction.DeleteLeft)
                || (rightUnc && sub.Action == SyncAction.DeleteRight)
                || HasUncAction(sub, leftUnc, rightUnc, recycleOverwritten))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasNetworkAction(
        DirectoryComparison dir,
        string leftBase,
        string rightBase,
        bool leftNoBin,
        bool rightNoBin,
        bool recycleOverwritten,
        Dictionary<string, bool> cache,
        Func<string, bool> probe,
        bool isDefaultProbe)
    {
        var dirLeft = CombineBase(leftBase, dir.RelativePath);
        var dirRight = CombineBase(rightBase, dir.RelativePath);

        foreach (var file in dir.Files)
        {
            if ((file.Action == SyncAction.DeleteLeft || (recycleOverwritten && file.Action == SyncAction.CopyToLeft && file.Status == ComparisonStatus.Modified))
                && (leftNoBin || !SupportsChild(dirLeft, Path.Combine(leftBase, file.RelativePath), cache, probe, isDefaultProbe)))
            {
                return true;
            }

            if ((file.Action == SyncAction.DeleteRight || (recycleOverwritten && file.Action == SyncAction.CopyToRight && file.Status == ComparisonStatus.Modified))
                && (rightNoBin || !SupportsChild(dirRight, Path.Combine(rightBase, file.RelativePath), cache, probe, isDefaultProbe)))
            {
                return true;
            }
        }

        foreach (var sub in dir.SubDirectories)
        {
            if (sub.Action == SyncAction.DeleteLeft
                && (leftNoBin || !SupportsPath(Path.Combine(leftBase, sub.RelativePath), cache, probe)))
            {
                return true;
            }

            if (sub.Action == SyncAction.DeleteRight
                && (rightNoBin || !SupportsPath(Path.Combine(rightBase, sub.RelativePath), cache, probe)))
            {
                return true;
            }

            if (HasNetworkAction(sub, leftBase, rightBase, leftNoBin, rightNoBin, recycleOverwritten, cache, probe, isDefaultProbe))
            {
                return true;
            }
        }

        return false;
    }

    private static string CombineBase(string basePath, string relativePath)
    {
        return string.IsNullOrEmpty(relativePath) ? basePath : Path.Combine(basePath, relativePath);
    }

    private static bool SupportsChild(
        string dirPath,
        string filePath,
        Dictionary<string, bool> cache,
        Func<string, bool> probe,
        bool isDefaultProbe)
    {
        if (!SupportsPath(dirPath, cache, probe))
        {
            return false;
        }

        if (!isDefaultProbe)
        {
            return SupportsPath(filePath, cache, probe);
        }

        return !IsReparsePoint(filePath) || SupportsPath(filePath, cache, probe);
    }

    private static bool SupportsPath(string path, Dictionary<string, bool> cache, Func<string, bool> probe)
    {
        if (cache.TryGetValue(path, out var supported))
        {
            return supported;
        }

        supported = probe(path);
        cache[path] = supported;
        return supported;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsUnderBase(string path, string basePath)
    {
        var trimmed = basePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (trimmed.Length == 0)
        {
            return false;
        }

        return path.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)
            && (path.Length == trimmed.Length
                || path[trimmed.Length] == Path.DirectorySeparatorChar
                || path[trimmed.Length] == Path.AltDirectorySeparatorChar);
    }
}
