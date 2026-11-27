using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Security;

namespace SpaceSnoop.Core;

public sealed class NetworkDriveRestore : IDisposable
{
    private const string ExtendedPrefix = @"\\?\";

    private readonly CancellationTokenSource _shutdown = new();
    private volatile Task<RestoreState>? _letters;

    public void Start(bool isElevated, ILogger logger)
    {
        Start(isElevated, NetworkDrives.ReadPersistent, NetworkDrives.PresentDriveNames, NetworkDrives.Connect, logger);
    }

    public async IAsyncEnumerable<DriveRestoreResult> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_letters is not { } lettersTask)
        {
            yield break;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, cancellationToken);
        var state = await lettersTask.WaitAsync(linked.Token).ConfigureAwait(false);

        if (state.ByLetter.Count == 0)
        {
            yield break;
        }

        await foreach (var completed in Task.WhenEach(state.ByLetter.Values).WithCancellation(linked.Token).ConfigureAwait(false))
        {
            yield return await completed.ConfigureAwait(false);
        }
    }

    public Task WaitForPathAsync(string path, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_letters is not { } stateTask)
        {
            return Task.CompletedTask;
        }

        var hasLetter = TryGetDriveLetter(path, out var letter);
        string? shareRoot = null;

        if (!hasLetter && !UncPath.TryParseShareRoot(path, out shareRoot))
        {
            return Task.CompletedTask;
        }

        if (stateTask.IsCompletedSuccessfully)
        {
            return HasPendingTask(stateTask.Result, hasLetter, letter, shareRoot)
                ? WaitCoreAsync(stateTask, hasLetter, letter, shareRoot, timeout, cancellationToken)
                : Task.CompletedTask;
        }

        return WaitCoreAsync(stateTask, hasLetter, letter, shareRoot, timeout, cancellationToken);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    internal void Start(
        bool isElevated,
        Func<IReadOnlyList<PersistentDrive>> readPersistent,
        Func<IEnumerable<string>> presentDrives,
        Func<PersistentDrive, int> connect,
        ILogger logger)
    {
        if (!isElevated)
        {
            _letters = null;
            return;
        }

        var token = _shutdown.Token;
        _letters = Task.Run(() => StartLetters(readPersistent, presentDrives, connect, logger, token), token);
    }

    internal static bool TryGetDriveLetter(string path, out char letter)
    {
        letter = '\0';

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var candidate = path.Trim();

        if (candidate.StartsWith(ExtendedPrefix, StringComparison.Ordinal))
        {
            candidate = candidate[ExtendedPrefix.Length..];
        }
        else
        {
            try
            {
                candidate = Path.GetFullPath(candidate);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
            {
                return false;
            }
        }

        if (candidate.Length < 2 || !char.IsAsciiLetter(candidate[0]) || candidate[1] != ':')
        {
            return false;
        }

        letter = char.ToUpperInvariant(candidate[0]);
        return true;
    }

    private static RestoreState StartLetters(
        Func<IReadOnlyList<PersistentDrive>> readPersistent,
        Func<IEnumerable<string>> presentDrives,
        Func<PersistentDrive, int> connect,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var missing = NetworkDrives.SelectMissing(readPersistent(), presentDrives());

        if (missing.Count == 0)
        {
            return RestoreState.Empty;
        }

        var byLetter = new Dictionary<char, Task<DriveRestoreResult>>(missing.Count);
        var byShareRoot = new Dictionary<string, List<Task<DriveRestoreResult>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var drive in missing)
        {
            var task = Task.Run(() => NetworkDrives.Restore(drive, connect, logger), cancellationToken);
            byLetter[char.ToUpperInvariant(drive.LocalName[0])] = task;

            if (UncPath.TryParseShareRoot(drive.RemotePath, out var shareRoot))
            {
                if (!byShareRoot.TryGetValue(shareRoot, out var list))
                {
                    list = [];
                    byShareRoot[shareRoot] = list;
                }

                list.Add(task);
            }
        }

        var shareMap = byShareRoot.ToDictionary(
            static x => x.Key,
            static IReadOnlyList<Task<DriveRestoreResult>> (x) => x.Value,
            StringComparer.OrdinalIgnoreCase);

        return new(byLetter, shareMap);
    }

    private static bool HasPendingTask(RestoreState state, bool hasLetter, char letter, string? shareRoot)
    {
        if (hasLetter)
        {
            return state.ByLetter.TryGetValue(letter, out var pending) && !pending.IsCompleted;
        }

        return shareRoot is not null
            && state.ByShareRoot.TryGetValue(shareRoot, out var tasks)
            && tasks.Any(static x => !x.IsCompleted);
    }

    private async Task WaitCoreAsync(
        Task<RestoreState> stateTask,
        bool hasLetter,
        char letter,
        string? shareRoot,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(timeout);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, cancellationToken, deadline.Token);

        try
        {
            var state = await stateTask.WaitAsync(combined.Token).ConfigureAwait(false);

            if (hasLetter)
            {
                if (state.ByLetter.TryGetValue(letter, out var restoreTask))
                {
                    await restoreTask.WaitAsync(combined.Token).ConfigureAwait(false);
                }

                return;
            }

            if (shareRoot is not null && state.ByShareRoot.TryGetValue(shareRoot, out var tasks))
            {
                await Task.WhenAll(tasks).WaitAsync(combined.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_shutdown.IsCancellationRequested)
        {
        }
    }

    private sealed record RestoreState(
        IReadOnlyDictionary<char, Task<DriveRestoreResult>> ByLetter,
        IReadOnlyDictionary<string, IReadOnlyList<Task<DriveRestoreResult>>> ByShareRoot)
    {
        public static RestoreState Empty { get; } = new(
            new Dictionary<char, Task<DriveRestoreResult>>(),
            new Dictionary<string, IReadOnlyList<Task<DriveRestoreResult>>>(StringComparer.OrdinalIgnoreCase));
    }
}
