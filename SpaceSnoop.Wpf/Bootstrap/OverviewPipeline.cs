using System.IO;
using System.Security;

namespace SpaceSnoop.Wpf.Bootstrap;

public readonly record struct OverviewPreflight(OverviewRunStatus Status, string? Reason = null);

public static class OverviewPipeline
{
    private const string TimeoutMessage = "Сетевой каталог не отвечает (превышено время ожидания).";

    internal static Func<string, bool> DirectoryEmpty { get; set; } = IsDirectoryEmpty;

    public static OverviewPreflight? Classify(SyncProfile profile)
    {
        var left = profile.Left.Trim();
        var right = profile.Right.Trim();

        if (left.Length == 0 || right.Length == 0 || UncPath.IsIncompleteUnc(left) || UncPath.IsIncompleteUnc(right))
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

    public static Task<OverviewPreflight?> ClassifyAsync(SyncProfile profile, CancellationToken cancellationToken = default)
    {
        return ClassifyAsync(profile, AppDefaults.PathProbeTimeoutMs, cancellationToken);
    }

    public static Task<OverviewPreflight?> ClassifyForSyncAsync(SyncProfile profile, CancellationToken cancellationToken = default)
    {
        return ClassifyForSyncAsync(profile, AppDefaults.PathProbeTimeoutMs, cancellationToken);
    }

    internal static async Task<OverviewPreflight?> ClassifyAsync(
        SyncProfile profile,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var left = profile.Left.Trim();
        var right = profile.Right.Trim();

        if (left.Length == 0 || right.Length == 0 || UncPath.IsIncompleteUnc(left) || UncPath.IsIncompleteUnc(right))
        {
            return new(OverviewRunStatus.Unavailable);
        }

        if (await CheckSourcesMissingAsync(left, right, HeadlessSync.MapMode(profile.Mode), timeoutMs, cancellationToken).ConfigureAwait(false) is { } missing)
        {
            return missing;
        }

        string? refusal;

        try
        {
            refusal = await Task.Run(() => SyncRoots.Refusal(left, right), cancellationToken)
                .WaitAsync(TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs)), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new(OverviewRunStatus.Unavailable, TimeoutMessage);
        }

        if (refusal is not null)
        {
            return new(OverviewRunStatus.Overlap, refusal);
        }

        return null;
    }

    internal static async Task<OverviewPreflight?> ClassifyForSyncAsync(
        SyncProfile profile,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        if (await ClassifyAsync(profile, timeoutMs, cancellationToken).ConfigureAwait(false) is { } preflight)
        {
            return preflight;
        }

        if (profile.Mirror
            && SyncProfile.MirrorSource(HeadlessSync.MapMode(profile.Mode), profile.Winner, profile.Left.Trim(), profile.Right.Trim()) is { } mirrorSource)
        {
            try
            {
                var empty = await Task.Run(() => DirectoryEmpty(mirrorSource), cancellationToken)
                    .WaitAsync(TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs)), cancellationToken)
                    .ConfigureAwait(false);

                if (empty)
                {
                    return new(OverviewRunStatus.Error, "Зеркало отменено: источник пуст.");
                }
            }
            catch (TimeoutException)
            {
                return new(OverviewRunStatus.Error, TimeoutMessage);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException or PathTooLongException)
            {
                return new(OverviewRunStatus.Error, exception.Message);
            }
        }

        return null;
    }

    private static async Task<OverviewPreflight?> CheckSourcesMissingAsync(
        string left,
        string right,
        SyncMode mode,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        if (mode == SyncMode.RightToLeft)
        {
            return await ProbeRequiredAsync(right, timeoutMs, cancellationToken).ConfigureAwait(false);
        }

        if (mode == SyncMode.Bidirectional)
        {
            var leftResult = await ProbeDirectoryAsync(left, timeoutMs, cancellationToken).ConfigureAwait(false);

            if (leftResult == PathProbeStatus.Exists)
            {
                return null;
            }

            var rightResult = await ProbeDirectoryAsync(right, timeoutMs, cancellationToken).ConfigureAwait(false);

            if (rightResult == PathProbeStatus.Exists)
            {
                return null;
            }

            return leftResult == PathProbeStatus.TimedOut || rightResult == PathProbeStatus.TimedOut
                ? new(OverviewRunStatus.Unavailable, TimeoutMessage)
                : new(OverviewRunStatus.Unavailable);
        }

        return await ProbeRequiredAsync(left, timeoutMs, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<OverviewPreflight?> ProbeRequiredAsync(string path, int timeoutMs, CancellationToken cancellationToken)
    {
        var status = await ProbeDirectoryAsync(path, timeoutMs, cancellationToken).ConfigureAwait(false);

        if (status == PathProbeStatus.Exists)
        {
            return null;
        }

        return status == PathProbeStatus.TimedOut
            ? new(OverviewRunStatus.Unavailable, TimeoutMessage)
            : new(OverviewRunStatus.Unavailable);
    }

    private static async Task<PathProbeStatus> ProbeDirectoryAsync(string path, int timeoutMs, CancellationToken cancellationToken)
    {
        var result = await PathProbe.CheckAsync(path, timeoutMs, cancellationToken).ConfigureAwait(false);

        if (result.Status == PathProbeStatus.Cancelled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return result.Status;
    }

    private static bool IsDirectoryEmpty(string path)
    {
        return !Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any();
    }
}
