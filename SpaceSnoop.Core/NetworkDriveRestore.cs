using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Security;

namespace SpaceSnoop.Core;

public sealed class NetworkDriveRestore : IDisposable
{
    private const string ExtendedPrefix = @"\\?\";

    private readonly CancellationTokenSource _shutdown = new();
    private volatile Task<IReadOnlyDictionary<char, Task<DriveRestoreResult>>>? _letters;

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
        var letters = await lettersTask.WaitAsync(linked.Token).ConfigureAwait(false);

        if (letters.Count == 0)
        {
            yield break;
        }

        await foreach (var completed in Task.WhenEach(letters.Values).WithCancellation(linked.Token).ConfigureAwait(false))
        {
            yield return await completed.ConfigureAwait(false);
        }
    }

    public Task WaitForPathAsync(string path, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_letters is not { } lettersTask || !TryGetDriveLetter(path, out var letter))
        {
            return Task.CompletedTask;
        }

        if (lettersTask.IsCompletedSuccessfully)
        {
            return lettersTask.Result.TryGetValue(letter, out var pending) && !pending.IsCompleted
                ? WaitCoreAsync(lettersTask, letter, timeout, cancellationToken)
                : Task.CompletedTask;
        }

        return WaitCoreAsync(lettersTask, letter, timeout, cancellationToken);
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

    private static IReadOnlyDictionary<char, Task<DriveRestoreResult>> StartLetters(
        Func<IReadOnlyList<PersistentDrive>> readPersistent,
        Func<IEnumerable<string>> presentDrives,
        Func<PersistentDrive, int> connect,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var missing = NetworkDrives.SelectMissing(readPersistent(), presentDrives());

        if (missing.Count == 0)
        {
            return new Dictionary<char, Task<DriveRestoreResult>>();
        }

        var tasks = new Dictionary<char, Task<DriveRestoreResult>>(missing.Count);

        foreach (var drive in missing)
        {
            tasks[char.ToUpperInvariant(drive.LocalName[0])] = Task.Run(() => NetworkDrives.Restore(drive, connect, logger), cancellationToken);
        }

        return tasks;
    }

    private async Task WaitCoreAsync(
        Task<IReadOnlyDictionary<char, Task<DriveRestoreResult>>> lettersTask,
        char letter,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(timeout);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, cancellationToken, deadline.Token);

        try
        {
            var letters = await lettersTask.WaitAsync(combined.Token).ConfigureAwait(false);

            if (letters.TryGetValue(letter, out var restoreTask))
            {
                await restoreTask.WaitAsync(combined.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_shutdown.IsCancellationRequested)
        {
        }
    }
}
