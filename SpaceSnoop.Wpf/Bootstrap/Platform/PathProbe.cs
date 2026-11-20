using System.IO;
using System.Security;

namespace SpaceSnoop.Wpf.Bootstrap.Platform;

public enum PathProbeStatus
{
    None = 0,
    Empty = 1,
    IncompleteUnc = 2,
    Exists = 3,
    Missing = 4,
    TimedOut = 5,
    Cancelled = 6,
}

public readonly record struct PathProbeResult(PathProbeStatus Status, string NormalizedPath);

public static class PathProbe
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, RootSlot> InFlight = new(StringComparer.OrdinalIgnoreCase);

    internal static Func<string, bool> DirectoryExists { get; set; } = Directory.Exists;

    public static Task<PathProbeResult> CheckAsync(string? path, CancellationToken cancellationToken = default)
    {
        return CheckAsync(path, DirectoryExists, AppDefaults.PathProbeTimeoutMs, cancellationToken);
    }

    public static Task<PathProbeResult> CheckAsync(string? path, int timeoutMs, CancellationToken cancellationToken = default)
    {
        return CheckAsync(path, DirectoryExists, timeoutMs, cancellationToken);
    }

    internal static Task<PathProbeResult> CheckAsync(
        string? path,
        Func<string, bool> probe,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(probe);

        var trimmed = path?.Trim() ?? string.Empty;

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new PathProbeResult(PathProbeStatus.Cancelled, trimmed));
        }

        if (trimmed.Length == 0)
        {
            return Task.FromResult(new PathProbeResult(PathProbeStatus.Empty, string.Empty));
        }

        if (UncPath.IsIncompleteUnc(trimmed))
        {
            return Task.FromResult(new PathProbeResult(PathProbeStatus.IncompleteUnc, trimmed));
        }

        var root = RootKey(trimmed);
        PendingProbe request;

        lock (Gate)
        {
            if (!InFlight.TryGetValue(root, out var slot) || slot.Running.IsCompleted)
            {
                request = new(trimmed, probe, new(TaskCreationOptions.RunContinuationsAsynchronously));
                var queue = new Queue<PendingProbe>();
                var running = Task.Run(() => RunProbeLoop(root, request), CancellationToken.None);
                InFlight[root] = new(running, queue);
            }
            else
            {
                PruneAbandoned(slot.Queue);
                request = new(trimmed, probe, new(TaskCreationOptions.RunContinuationsAsynchronously));
                slot.Queue.Enqueue(request);
            }
        }

        return WaitResultAsync(request, timeoutMs, cancellationToken);
    }

    internal static string RootKey(string path)
    {
        if (UncPath.TryParseShareRoot(path, out var shareRoot))
        {
            return shareRoot;
        }

        try
        {
            return Path.GetPathRoot(path) is { Length: > 0 } root ? root : path;
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    private static async Task<PathProbeResult> WaitResultAsync(
        PendingProbe request,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        try
        {
            var timeout = TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs));
            return await request.Completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            request.MarkAbandoned();
            return new(PathProbeStatus.TimedOut, request.Path);
        }
        catch (OperationCanceledException)
        {
            request.MarkAbandoned();
            return new(PathProbeStatus.Cancelled, request.Path);
        }
    }

    private static void RunProbeLoop(string root, PendingProbe first)
    {
        var current = first;

        while (current is not null)
        {
            if (current.IsAbandoned)
            {
                current.Completion.TrySetResult(new(PathProbeStatus.Cancelled, current.Path));
            }
            else
            {
                var status = SafeProbe(current.Probe, current.Path)
                    ? PathProbeStatus.Exists
                    : PathProbeStatus.Missing;
                current.Completion.TrySetResult(new(status, current.Path));
            }

            current = DequeueNext(root);
        }
    }

    private static PendingProbe? DequeueNext(string root)
    {
        lock (Gate)
        {
            if (!InFlight.TryGetValue(root, out var slot))
            {
                return null;
            }

            while (slot.Queue.TryDequeue(out var next))
            {
                if (!next.IsAbandoned)
                {
                    return next;
                }

                next.Completion.TrySetResult(new(PathProbeStatus.Cancelled, next.Path));
            }

            InFlight.Remove(root);
            return null;
        }
    }

    private static void PruneAbandoned(Queue<PendingProbe> queue)
    {
        var count = queue.Count;

        for (var i = 0; i < count; i++)
        {
            var item = queue.Dequeue();

            if (!item.IsAbandoned)
            {
                queue.Enqueue(item);
            }
        }
    }

    private static bool SafeProbe(Func<string, bool> probe, string path)
    {
        try
        {
            return probe(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            return false;
        }
    }

    private sealed class PendingProbe(
        string path,
        Func<string, bool> probe,
        TaskCompletionSource<PathProbeResult> completion)
    {
        private int _abandoned;

        public string Path { get; } = path;

        public Func<string, bool> Probe { get; } = probe;

        public TaskCompletionSource<PathProbeResult> Completion { get; } = completion;

        public bool IsAbandoned => Volatile.Read(ref _abandoned) != 0;

        public void MarkAbandoned()
        {
            Interlocked.Exchange(ref _abandoned, 1);
        }
    }

    private readonly record struct RootSlot(Task Running, Queue<PendingProbe> Queue);
}
