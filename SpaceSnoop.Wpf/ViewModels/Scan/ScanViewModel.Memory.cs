using System.Diagnostics;
using System.IO;
using System.Security;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

internal enum ScanMemoryAction
{
    None = 0,
    Compact = 1,
    ReclaimMft = 2,
}

internal enum ScanMemoryReturnOutcome
{
    None = 0,
    Skipped = 1,
    Cancelled = 2,
    Completed = 3,
}

public sealed partial class ScanViewModel
{
    private bool _isPreparing;
    private DeferredMemoryReturn? _memoryReturn;

    internal Task<ScanMemoryReturnOutcome> PendingMemoryReturn { get; private set; } = Task.FromResult(ScanMemoryReturnOutcome.None);

    internal static bool ShouldReturnScanMemory(long loadBytes, long thresholdBytes)
    {
        return thresholdBytes > 0 && loadBytes >= thresholdBytes;
    }

    internal static ScanMemoryAction SelectScanMemoryAction(
        bool returnMemoryAfterScan,
        bool usedMft,
        long loadBytes,
        long thresholdBytes)
    {
        if (returnMemoryAfterScan)
        {
            return usedMft || ShouldReturnScanMemory(loadBytes, thresholdBytes)
                ? ScanMemoryAction.Compact
                : ScanMemoryAction.None;
        }

        return usedMft ? ScanMemoryAction.ReclaimMft : ScanMemoryAction.None;
    }

    private async Task ScanAfterRescanPromptAsync(string path, bool knownExists = false)
    {
        if (string.IsNullOrWhiteSpace(path) || UncPath.IsIncompleteUnc(path) || IsBusy || _isPreparing)
        {
            return;
        }

        if (Preferences.ReleaseBeforeRescan
            && Preferences.ConfirmRescan
            && Roots.Count > 0
            && TryGetUnmarkedRootCandidates(path, out var normalized, out var candidates))
        {
            SetPreparing(true);
            PathProbeResult probe;
            string? missingMessage = null;
            bool shouldAsk;

            try
            {
                if (knownExists)
                {
                    probe = new(PathProbeStatus.Exists, path);
                }
                else
                {
                    probe = await ScanTargetProbe.CheckAsync(path, CancellationToken.None);

                    if (probe.Status is not PathProbeStatus.Exists and not PathProbeStatus.TimedOut)
                    {
                        missingMessage = await ScanTargetProbe.DescribeMissingAsync(path, CancellationToken.None);
                    }
                }

                shouldAsk = probe.Status == PathProbeStatus.Exists
                    && candidates.Any(rootPath => ScanTreeEditor.SamePath(rootPath, normalized));
            }
            finally
            {
                SetPreparing(false);
            }

            if (IsBusy)
            {
                return;
            }

            if (probe.Status == PathProbeStatus.TimedOut)
            {
                _uiDispatcher.Invoke(() => _dialogs.Warning("Сканирование", ScanTargetProbe.FormatTimeoutMessage(path)));
                return;
            }

            if (probe.Status != PathProbeStatus.Exists)
            {
                _uiDispatcher.Invoke(() => _dialogs.Warning("Сканирование", missingMessage ?? $"Каталог не найден: {path}"));
                return;
            }

            if (shouldAsk)
            {
                var dialog = RescanPrompt.BuildConfirm(path);

                if (!await _dialogs.ShowAsync(dialog))
                {
                    return;
                }

                if (ReferenceEquals(dialog.Chosen, RescanPrompt.StopAsking))
                {
                    Preferences.ConfirmRescan = false;
                }

                if (IsBusy)
                {
                    return;
                }
            }
        }

        if (await ScanAsync(path, CancellationToken.None))
        {
            Tips.OnScanCompleted();
        }
    }

    private void SetPreparing(bool value)
    {
        if (_isPreparing == value)
        {
            return;
        }

        _isPreparing = value;
        StartCommand.NotifyCanExecuteChanged();
        BrowseCommand.NotifyCanExecuteChanged();
        RescanCancelledCommand.NotifyCanExecuteChanged();
    }

    private bool TryGetUnmarkedRootCandidates(string path, out string normalized, out string[] candidates)
    {
        normalized = string.Empty;
        candidates = [];

        try
        {
            normalized = Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            return false;
        }

        var normalizedTarget = normalized;
        var marksPresent = Marks.HasMarked;

        candidates = Roots
            .Where(root => !(marksPresent && root.Space is { } space && ScanTreeEditor.HasMarkedSelfOrChild(space)))
            .Select(static root => root.AbsolutePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Where(rootPath => string.Equals(rootPath, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return candidates.Length > 0;
    }

    private bool ReleaseBeforeScan(DirectoryInfo directory)
    {
        if (!ReleasePreviousResult(directory))
        {
            return false;
        }

        if (_runner.HasUsnCacheFor(directory.FullName))
        {
            return true;
        }

        var collect = Stopwatch.StartNew();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, false, false);
        _logger.ScanPreviousReleased(directory.FullName, collect.ElapsedMilliseconds);

        return true;
    }

    private bool ReleasePreviousResult(DirectoryInfo directory)
    {
        if (!Preferences.ReleaseBeforeRescan)
        {
            return false;
        }

        var current = CurrentRoot;
        var released = ScanTreeEditor.ReleaseUnmarkedRoot(Roots, directory.FullName, Marks.HasMarked, out var keptMarked);

        if (keptMarked)
        {
            _logger.ScanPreviousKeptForMarks(directory.FullName);
        }

        if (released.Count == 0)
        {
            return false;
        }

        var spaces = released.Select(static root => root.Space).OfType<SpaceBase>().ToHashSet<SpaceBase>(ReferenceEqualityComparer.Instance);

        Treemap.RefreshAfterDeletion(spaces);

        if (current is not null && spaces.Contains(current))
        {
            Duplicates.Clear();
            HasResult = false;
        }

        return true;
    }

    private void CancelPendingMemoryReturn()
    {
        Interlocked.Exchange(ref _memoryReturn, null)?.Cancel();
    }

    internal Task<ScanMemoryReturnOutcome> ScheduleReturnScanMemory(DirectorySpace result, bool usedMft)
    {
        return ScheduleReturnScanMemory(result, usedMft, Volatile.Read(ref _scanVersion));
    }

    internal Task<ScanMemoryReturnOutcome> ScheduleReturnScanMemory(DirectorySpace result, bool usedMft, bool usedUsn)
    {
        return ScheduleReturnScanMemory(result, usedMft, Volatile.Read(ref _scanVersion), usedUsn);
    }

    internal Task<ScanMemoryReturnOutcome> ScheduleReturnScanMemory(
        DirectorySpace result,
        bool usedMft,
        int expectedVersion,
        bool usedUsn = false)
    {
        if (expectedVersion != Volatile.Read(ref _scanVersion))
        {
            return Task.FromResult(ScanMemoryReturnOutcome.Cancelled);
        }

        CancelPendingMemoryReturn();

        if (((long)result.TotalFileCount + result.TotalDirectoryCount) < AppDefaults.ScanMemoryReturnMinNodes)
        {
            var skipped = Task.FromResult(ScanMemoryReturnOutcome.Skipped);
            PendingMemoryReturn = skipped;
            return skipped;
        }

        var reclaimMft = usedMft && !usedUsn;
        var returnMemoryAfterScan = Preferences.ReturnMemoryAfterScan;

        if (!returnMemoryAfterScan && !reclaimMft)
        {
            var skipped = Task.FromResult(ScanMemoryReturnOutcome.Skipped);
            PendingMemoryReturn = skipped;
            return skipped;
        }

        var handle = new DeferredMemoryReturn();
        Interlocked.Exchange(ref _memoryReturn, handle)?.Cancel();

        if (expectedVersion != Volatile.Read(ref _scanVersion))
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _memoryReturn, null, handle), handle))
            {
                handle.Cancel();
            }

            handle.Release();
            return Task.FromResult(ScanMemoryReturnOutcome.Cancelled);
        }

        var task = RunDeferredReturnScanMemoryAsync(result, reclaimMft, expectedVersion, handle);
        PendingMemoryReturn = task;
        return task;
    }

    private async Task<ScanMemoryReturnOutcome> RunDeferredReturnScanMemoryAsync(
        DirectorySpace result,
        bool usedMft,
        int expectedVersion,
        DeferredMemoryReturn handle)
    {
        try
        {
            return await ReturnScanMemoryAsync(result, usedMft, expectedVersion, reportPhase: null, handle.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (handle.Token.IsCancellationRequested)
        {
            return ScanMemoryReturnOutcome.Cancelled;
        }
        finally
        {
            Interlocked.CompareExchange(ref _memoryReturn, null, handle);
            handle.Release();
        }
    }

    private Task<ScanMemoryReturnOutcome> ReturnScanMemoryAsync(
        DirectorySpace result,
        bool usedMft,
        Action<string>? reportPhase,
        CancellationToken cancellationToken)
    {
        return ReturnScanMemoryAsync(result, usedMft, Volatile.Read(ref _scanVersion), reportPhase, cancellationToken);
    }

    private async Task<ScanMemoryReturnOutcome> ReturnScanMemoryAsync(
        DirectorySpace result,
        bool usedMft,
        int expectedVersion,
        Action<string>? reportPhase,
        CancellationToken cancellationToken,
        bool usedUsn = false)
    {
        if (((long)result.TotalFileCount + result.TotalDirectoryCount) < AppDefaults.ScanMemoryReturnMinNodes)
        {
            return ScanMemoryReturnOutcome.Skipped;
        }

        var reclaimMft = usedMft && !usedUsn;
        var returnMemoryAfterScan = Preferences.ReturnMemoryAfterScan;

        if (!returnMemoryAfterScan && !reclaimMft)
        {
            return ScanMemoryReturnOutcome.Skipped;
        }

        var pressure = GC.GetGCMemoryInfo();
        var action = SelectScanMemoryAction(
            returnMemoryAfterScan,
            reclaimMft,
            pressure.MemoryLoadBytes,
            pressure.HighMemoryLoadThresholdBytes);

        if (action == ScanMemoryAction.None)
        {
            if (returnMemoryAfterScan)
            {
                _logger.ScanMemoryReturnSkipped(pressure.MemoryLoadBytes, pressure.HighMemoryLoadThresholdBytes);
            }

            return ScanMemoryReturnOutcome.Skipped;
        }

        if (expectedVersion != Volatile.Read(ref _scanVersion) || cancellationToken.IsCancellationRequested)
        {
            return ScanMemoryReturnOutcome.Cancelled;
        }

        if (action == ScanMemoryAction.Compact)
        {
            reportPhase?.Invoke("Ожидание возврата памяти");
        }

        try
        {
            await Task.Delay(AppDefaults.ScanMemoryReturnDelayMs, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ScanMemoryReturnOutcome.Cancelled;
        }

        if (expectedVersion != Volatile.Read(ref _scanVersion) || cancellationToken.IsCancellationRequested)
        {
            return ScanMemoryReturnOutcome.Cancelled;
        }

        bool collected;

        try
        {
            if (action == ScanMemoryAction.Compact)
            {
                if (!reclaimMft)
                {
                    pressure = GC.GetGCMemoryInfo();

                    if (!ShouldReturnScanMemory(pressure.MemoryLoadBytes, pressure.HighMemoryLoadThresholdBytes))
                    {
                        return ScanMemoryReturnOutcome.Skipped;
                    }
                }

                reportPhase?.Invoke("Возврат памяти после сканирования");

                collected = await Task.Run(
                        () =>
                        {
                            if (expectedVersion != Volatile.Read(ref _scanVersion) || cancellationToken.IsCancellationRequested)
                            {
                                return false;
                            }

                            var collect = Stopwatch.StartNew();
                            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
                            var memory = GC.GetGCMemoryInfo();

                            _logger.ScanMemoryReturned(collect.ElapsedMilliseconds, memory.HeapSizeBytes, memory.TotalCommittedBytes);
                            return true;
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                collected = await Task.Run(
                        () =>
                        {
                            if (expectedVersion != Volatile.Read(ref _scanVersion) || cancellationToken.IsCancellationRequested)
                            {
                                return false;
                            }

                            var collect = Stopwatch.StartNew();
                            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, false, false);
                            var memory = GC.GetGCMemoryInfo();

                            _logger.ScanMftMemoryReclaimRequested(collect.ElapsedMilliseconds, memory.HeapSizeBytes, memory.TotalCommittedBytes);
                            return true;
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ScanMemoryReturnOutcome.Cancelled;
        }

        if (!collected)
        {
            return ScanMemoryReturnOutcome.Cancelled;
        }

        if (reportPhase is not null)
        {
            await _performance.FlushFramesAsync().ConfigureAwait(false);
        }

        return ScanMemoryReturnOutcome.Completed;
    }

    private sealed class DeferredMemoryReturn
    {
        private readonly Lock _gate = new();
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;

        public DeferredMemoryReturn()
        {
            Token = _cts.Token;
        }

        public CancellationToken Token { get; }

        public void Cancel()
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    _cts.Cancel();
                }
            }
        }

        public void Release()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _cts.Dispose();
            }
        }
    }
}
