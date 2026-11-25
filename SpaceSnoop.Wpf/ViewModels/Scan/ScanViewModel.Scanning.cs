using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel
{
    private async Task<bool> ScanAsync(string path, CancellationToken external = default)
    {
        path = path?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (UncPath.IsIncompleteUnc(path))
        {
            _dialogs.Warning("Сканирование", @"Укажите полный сетевой путь вида \\сервер\шара.");
            return false;
        }

        DirectoryInfo directory;

        try
        {
            directory = new(path);
        }
        catch (Exception exception)
        {
            var cause = exception.Unwrap();
            _logger.ScanFailed(cause, path);
            _dialogs.Error("Ошибка сканирования", ScanTargetProbe.DescribeScanError(cause, path));
            return false;
        }

        var targetPath = Path.TrimEndingDirectorySeparator(directory.FullName);
        CancelPendingMemoryReturn();
        using var scanCts = new CancellationTokenSource();
        _cts = scanCts;
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(scanCts.Token, external);
        Interlocked.Increment(ref _scanVersion);
        var token = runCts.Token;
        IsPickerOpen = false;
        IsScanning = true;
        ScanWasCancelled = false;
        ScanTargetPath = targetPath;
        StatusCaption = $"Сканирование: {targetPath}";

        SelectedNode = null;
        var released = false;
        var resultApplied = false;
        var progressFinished = false;
        Task? measurement = null;

        try
        {
            Progress.ReportPhase("Подготовка сканирования");

            if (_networkDrives is not null)
            {
                await _networkDrives.WaitForPathAsync(
                    targetPath,
                    TimeSpan.FromSeconds(AppDefaults.McpNetworkDriveTimeoutSeconds),
                    token);
            }

            var probe = await ScanTargetProbe.CheckAsync(targetPath, token);

            if (probe.Status == PathProbeStatus.Cancelled)
            {
                throw new OperationCanceledException(token);
            }

            if (probe.Status == PathProbeStatus.TimedOut)
            {
                _uiDispatcher.Invoke(() => _dialogs.Warning("Сканирование", ScanTargetProbe.FormatTimeoutMessage(targetPath)));
                return false;
            }

            if (probe.Status != PathProbeStatus.Exists)
            {
                var missingMessage = await ScanTargetProbe.DescribeMissingAsync(targetPath, token);
                _uiDispatcher.Invoke(() => _dialogs.Warning("Сканирование", missingMessage));
                return false;
            }

            string? refusal;

            try
            {
                refusal = await Task.Run(() => ScanTargetProbe.RootRefusal(directory), token)
                    .WaitAsync(TimeSpan.FromMilliseconds(AppDefaults.PathProbeTimeoutMs), token);
            }
            catch (TimeoutException)
            {
                _uiDispatcher.Invoke(() => _dialogs.Warning("Сканирование", ScanTargetProbe.FormatTimeoutMessage(targetPath)));
                return false;
            }

            token.ThrowIfCancellationRequested();

            if (refusal is not null)
            {
                _logger.ScanRootLinkRejected(targetPath);
                _uiDispatcher.Invoke(() => _dialogs.Warning("Сканирование", refusal));
                return false;
            }

            if (UncPath.IsUnc(targetPath))
            {
                NetworkDiscovery.Remember(targetPath);
            }

            var lastDriveChanged = await _uiDispatcher.InvokeAsync(() =>
            {
                var known = Drives.HasDrive(targetPath);
                Drives.AddDrive(targetPath);

                if (!known)
                {
                    Drives.LoadDriveLabels();
                }

                var changed = !_suppressPersist
                    && !string.Equals(_settings.GetStringValue(SettingsKeys.ScanLastDrive), targetPath, StringComparison.Ordinal);
                if (changed)
                {
                    _settings.SetValue(SettingsKeys.ScanLastDrive, targetPath);
                }

                released = ReleaseBeforeScan(directory);
                return changed;
            });

            var willUseMft = Preferences.MftEnabled
                && (!Preferences.MftRootOnly || ScanRunner.IsVolumeRoot(directory));
            var waitForMftFrame = willUseMft && !Preferences.MftRawDisk;

            var flushTask = lastDriveChanged
                ? Task.Run(() => Persist(_settings.Flush), CancellationToken.None)
                : Task.CompletedTask;
            var parallelismTask = Preferences.ResolveParallelismAsync(targetPath).WaitAsync(token);
            var estimateTask = willUseMft
                ? ScanProgressViewModel.EstimateTotalBytesAsync(directory, token)
                : Task.FromResult<long?>(null);
            var capacityTask = ReadCapacityAsync(directory.FullName, token);
            var frameTask = waitForMftFrame
                ? _performance.WaitForRenderedFrameAsync(token)
                : Task.CompletedTask;

            await Task.WhenAll(flushTask, parallelismTask, estimateTask, capacityTask, frameTask);
            token.ThrowIfCancellationRequested();

            var parallelism = await parallelismTask;
            var estimatedBytes = await estimateTask;
            var prefetchedCapacity = await capacityTask;

            var progress = Progress.Begin(
                directory,
                targetPath,
                parallelism,
                estimatedBytes,
                deferTimer: willUseMft,
                probeEstimate: !willUseMft);

            if (parallelism == 1 && Preferences.UseMultithreading && Preferences.MaxParallelism > 1)
            {
                _logger.ScanMediaLimited(targetPath, Preferences.MaxParallelism);
            }

            _logger.ScanStarted(targetPath, parallelism > 1, parallelism);

            var outcome = await RunTraversalAsync(directory, parallelism, progress, token);
            token.ThrowIfCancellationRequested();

            var elapsed = Progress.Finish(releaseMeasurement: false);
            progressFinished = true;
            var driveCapacity = string.Equals(
                Path.TrimEndingDirectorySeparator(outcome.Root.AbsolutePath),
                targetPath,
                StringComparison.OrdinalIgnoreCase)
                ? prefetchedCapacity
                : await ReadCapacityAsync(outcome.Root.AbsolutePath, token);
            var applied = Stopwatch.StartNew();

            Progress.ReportPhase("Показ результата сканирования");
            _uiDispatcher.Invoke(() =>
            {
                ApplyScanResult(outcome.Root, elapsed, Progress.Traversal, outcome.Notes, driveCapacity);
                var appliedVersion = Volatile.Read(ref _scanVersion);
                resultApplied = true;
                measurement = Progress.CompleteMeasurementAsync();
                ScheduleReturnScanMemory(outcome.Root, outcome.Notes.UsedMft, appliedVersion);
                Interlocked.CompareExchange(ref _cts, null, scanCts);
                IsScanning = false;
                StatusCaption = null;
                _notifier.Notify($"Сканирование завершено: {outcome.Root.AbsolutePath} · {outcome.Root.TotalSizeText}", StatusSeverity.Success);
            });

            _logger.ScanPhases((long)elapsed.TotalMilliseconds, (long)applied.Elapsed.TotalMilliseconds);
            return true;
        }
        catch (OperationCanceledException)
        {
            CancelledPath = path;
            CancelledReleasedPrevious = released;
            ScanWasCancelled = true;
            _logger.ScanCancelled(path);
            return false;
        }
        catch (Exception exception)
        {
            var cause = exception.Unwrap();
            var message = ScanTargetProbe.DescribeScanError(cause, path);
            _logger.ScanFailed(cause, path);
            _uiDispatcher.Invoke(() =>
            {
                _notifier.Notify($"Ошибка сканирования: {message}", StatusSeverity.Error);
                _dialogs.Error("Ошибка сканирования", message);
            });
            return false;
        }
        finally
        {
            Interlocked.CompareExchange(ref _cts, null, scanCts);

            if (!progressFinished)
            {
                Progress.Finish(releaseMeasurement: false);
            }

            measurement ??= Progress.CompleteMeasurementAsync();

            if (!resultApplied)
            {
                IsScanning = false;
                StatusCaption = null;
            }

            await measurement;
        }
    }

    private static async Task<DriveCapacity?> ReadCapacityAsync(string path, CancellationToken token)
    {
        try
        {
            return await Task.Run(() => DriveCapacity.TryRead(path), token)
                .WaitAsync(TimeSpan.FromMilliseconds(AppDefaults.PathProbeTimeoutMs), token);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }
}
