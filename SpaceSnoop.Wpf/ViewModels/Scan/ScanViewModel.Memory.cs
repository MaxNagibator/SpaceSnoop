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

public sealed partial class ScanViewModel
{
    private bool _isPreparing;

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

    private async Task ReturnScanMemoryAsync(
        DirectorySpace result,
        bool usedMft,
        Action<string> reportPhase,
        CancellationToken cancellationToken)
    {
        if (((long)result.TotalFileCount + result.TotalDirectoryCount) < AppDefaults.ScanMemoryReturnMinNodes)
        {
            return;
        }

        var returnMemoryAfterScan = Preferences.ReturnMemoryAfterScan;

        if (!returnMemoryAfterScan && !usedMft)
        {
            return;
        }

        var pressure = GC.GetGCMemoryInfo();
        var action = SelectScanMemoryAction(
            returnMemoryAfterScan,
            usedMft,
            pressure.MemoryLoadBytes,
            pressure.HighMemoryLoadThresholdBytes);

        if (action == ScanMemoryAction.None)
        {
            if (returnMemoryAfterScan)
            {
                _logger.ScanMemoryReturnSkipped(pressure.MemoryLoadBytes, pressure.HighMemoryLoadThresholdBytes);
            }

            return;
        }

        var version = _scanVersion;

        if (action == ScanMemoryAction.Compact)
        {
            reportPhase("Ожидание возврата памяти");
        }

        try
        {
            await Task.Delay(AppDefaults.ScanMemoryReturnDelayMs, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (version != _scanVersion || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (action == ScanMemoryAction.Compact)
        {
            if (!usedMft)
            {
                pressure = GC.GetGCMemoryInfo();

                if (!ShouldReturnScanMemory(pressure.MemoryLoadBytes, pressure.HighMemoryLoadThresholdBytes))
                {
                    return;
                }
            }

            reportPhase("Возврат памяти после сканирования");

            await Task.Run(
                () =>
                {
                    var collect = Stopwatch.StartNew();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
                    var memory = GC.GetGCMemoryInfo();

                    _logger.ScanMemoryReturned(collect.ElapsedMilliseconds, memory.HeapSizeBytes, memory.TotalCommittedBytes);
                },
                CancellationToken.None);
        }
        else
        {
            await Task.Run(
                () =>
                {
                    var collect = Stopwatch.StartNew();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, false, false);
                    var memory = GC.GetGCMemoryInfo();

                    _logger.ScanMftMemoryReclaimRequested(collect.ElapsedMilliseconds, memory.HeapSizeBytes, memory.TotalCommittedBytes);
                },
                CancellationToken.None);
        }

        await _performance.FlushFramesAsync();
    }
}
