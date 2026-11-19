using System.IO;
using System.Security;

namespace SpaceSnoop.Wpf.Bootstrap;

public readonly record struct OverviewPreflight(OverviewRunStatus Status, string? Reason = null);

public static class OverviewPipeline
{
    internal static Func<string, bool> DirectoryEmpty { get; set; } = IsDirectoryEmpty;

    public static OverviewPreflight? Classify(SyncProfile profile)
    {
        var left = profile.Left.Trim();
        var right = profile.Right.Trim();

        if (left.Length == 0 || right.Length == 0)
        {
            return new(OverviewRunStatus.Unavailable);
        }

        if (SyncProfile.SourceMissing(left, right, HeadlessSync.MapMode(profile.Mode)))
        {
            return new(OverviewRunStatus.Unavailable);
        }

        if (SyncRoots.Refusal(left, right) is { } refusal)
        {
            return new(OverviewRunStatus.Overlap, refusal);
        }

        return null;
    }

    public static OverviewPreflight? ClassifyForSync(SyncProfile profile)
    {
        if (Classify(profile) is { } preflight)
        {
            return preflight;
        }

        if (profile.Mirror
            && SyncProfile.MirrorSource(HeadlessSync.MapMode(profile.Mode), profile.Winner, profile.Left.Trim(), profile.Right.Trim()) is { } mirrorSource)
        {
            try
            {
                if (DirectoryEmpty(mirrorSource))
                {
                    return new(OverviewRunStatus.Error, "Зеркало отменено: источник пуст.");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException or PathTooLongException)
            {
                return new(OverviewRunStatus.Error, exception.Message);
            }
        }

        return null;
    }

    private static bool IsDirectoryEmpty(string path)
    {
        return !Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any();
    }
}
