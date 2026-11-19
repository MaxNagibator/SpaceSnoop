using System.IO;
using System.Runtime.InteropServices;
using System.Security;

namespace SpaceSnoop.Wpf.Bootstrap.Platform;

public static class VolumeSpace
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, RootReads> InFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Task<long?> Skipped = Task.FromResult<long?>(null);

    internal static Func<string, long?> Reader { get; set; } = Read;

    public static async Task<long?> ReadFreeAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || FullPathAndRoot(path) is not { } target)
        {
            return null;
        }

        try
        {
            return await Start(target.FullPath, target.Root).WaitAsync(TimeSpan.FromMilliseconds(AppDefaults.VolumeSpaceTimeoutMs)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private static Task<long?> Start(string fullPath, string root)
    {
        var reader = Reader;

        lock (Gate)
        {
            InFlight.TryGetValue(root, out var reads);

            if (reads.Waiting is { } promoted && reads.Running.Free.IsCompleted)
            {
                reads = new(promoted, null);
            }

            if (reads.Running.Free is not { IsCompleted: false } running)
            {
                var read = Task.Run(() => reader(fullPath));
                InFlight[root] = new(new(fullPath, read), null);
                return read;
            }

            if (SamePath(reads.Running.Path, fullPath))
            {
                return running;
            }

            if (reads.Waiting is { } waiting)
            {
                return SamePath(waiting.Path, fullPath) ? waiting.Free : Skipped;
            }

            var queued = running.ContinueWith(
                _ => reader(fullPath),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);

            InFlight[root] = new(reads.Running, new(fullPath, queued));
            return queued;
        }
    }

    private static bool SamePath(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    // TODO: граница тома – строковый корень пути: том, смонтированный в папку, стоит в одной очереди с диском этой папки,
    // а subst-диск и \\?\Volume{…} того же тома – в отдельной; при первом зависании через такой путь – ключ по GUID тома,
    // который фоновый замер узнаёт сам (GetVolumeNameForVolumeMountPoint) и кэширует по корню
    private static (string FullPath, string Root)? FullPathAndRoot(string path)
    {
        try
        {
            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
            return Path.GetPathRoot(fullPath) is { Length: > 0 } root ? (fullPath, root) : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            return null;
        }
    }

    private static long? Read(string path)
    {
        try
        {
            var current = Path.GetFullPath(path);

            while (!string.IsNullOrEmpty(current))
            {
                if (GetDiskFreeSpaceEx(WithSeparator(current), out var free, out _, out _))
                {
                    return (long)Math.Min(free, long.MaxValue);
                }

                current = Path.GetDirectoryName(current);
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or SecurityException or NotSupportedException)
        {
            return null;
        }
    }

    private static string WithSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailableToCaller,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);

    private readonly record struct PendingRead(string Path, Task<long?> Free);

    private readonly record struct RootReads(PendingRead Running, PendingRead? Waiting);
}
