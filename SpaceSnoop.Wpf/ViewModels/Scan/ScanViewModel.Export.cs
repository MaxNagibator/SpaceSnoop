using SpaceSnoop.Core.Export;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenPickerCommand))]
    private bool _isExporting;

    [ObservableProperty]
    private int _exportProgressPercent;

    [ObservableProperty]
    private string _exportProgressText = string.Empty;

    private CancellationTokenSource? _exportCancellation;

    private bool CanExportFullScan()
    {
        return HasResult && CurrentRoot is not null && !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanExportFullScan))]
    private async Task ExportFullScanAsync()
    {
        if (CurrentRoot is not { } root)
        {
            return;
        }

        var total = root.TotalFileCount + root.TotalDirectoryCount;
        var options = new ScanExportOptions(ScanExport.DefaultDepth, LastScanParallelism > 1, LastScanParallelism, LastScanNotes.Engine);
        var notes = ScanExportNotes.From(
            LastScanNotes.ExtraNameBytes,
            LastScanNotes.DroppedObjects,
            LastScanNotes.DroppedBytes,
            LastScanNotes.UnknownSizeFiles,
            LastScanNotes.PartialRecords);

        string? path = null;
        string? temporaryPath = null;
        CancellationTokenSource? cancellation = null;
        try
        {
            path = _filePicker.SaveFile(
                new("Сохранить скан", "JSON (*.json)|*.json")
                {
                    DefaultExtension = ".json",
                    FileName = BuildFullScanFileName(root.AbsolutePath),
                });

            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            path = Path.GetFullPath(path);
            var temporaryFile = Path.Combine(Path.GetDirectoryName(path)!, $"spacesnoop-{Guid.NewGuid():N}.tmp");
            temporaryPath = temporaryFile;
            var cancellationSource = new CancellationTokenSource();
            cancellation = cancellationSource;
            _exportCancellation = cancellationSource;
            ExportProgressPercent = 0;
            ExportProgressText = $"Подготовка {total:N0} записей";
            StatusCaption = $"Сохранение полного скана: {ExportProgressText}";
            IsExporting = true;

            var progress = new Progress<int>(completed =>
            {
                ExportProgressPercent = total == 0 ? 100 : (int)Math.Min(100L, completed * 100L / total);
                ExportProgressText = $"Обработано {completed:N0} из {total:N0}";
            });

            await Task.Run(
                () =>
                {
                    using var output = new FileStream(temporaryFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.SequentialScan);
                    FullScanExport.WriteJson(output, root, root.AbsolutePath, options, AppInfo.Version, notes, progress, cancellationSource.Token);
                },
                cancellationSource.Token);

            cancellationSource.Token.ThrowIfCancellationRequested();
            File.Move(temporaryFile, path, true);
            ExportProgressPercent = 100;
            ExportProgressText = $"Сохранено: {root.TotalFileCount:N0} файлов, {root.TotalDirectoryCount:N0} каталогов";
            _logger.ScanExportCompleted(path, root.TotalFileCount, root.TotalDirectoryCount);
            _notifier.Notify($"Скан сохранён: {Path.GetFileName(path)} · {root.TotalFileCount:N0} файлов", StatusSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            _notifier.Notify("Экспорт скана отменён");
        }
        catch (Exception exception)
        {
            _logger.ScanExportFailed(exception, path ?? string.Empty);
            _dialogs.Error("Экспорт сканирования", exception.Message);
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception)
                {
                    _logger.ScanExportTemporaryFileCleanupFailed(exception, temporaryPath);
                }
            }

            cancellation?.Dispose();
            _exportCancellation = null;

            if (IsExporting)
            {
                StatusCaption = null;
                IsExporting = false;
            }
        }
    }

    private bool CanCancelFullScanExport()
    {
        return IsExporting;
    }

    [RelayCommand(CanExecute = nameof(CanCancelFullScanExport))]
    private void CancelFullScanExport()
    {
        _exportCancellation?.Cancel();
    }

    private static string BuildFullScanFileName(string rootPath)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(rootPath);
        var name = Path.GetFileName(trimmed);

        if (name.Length == 0)
        {
            name = Path.GetPathRoot(rootPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace(":", string.Empty) ?? "root";
        }

        var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return $"scan-{(safe.Length == 0 ? "root" : safe)}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
    }

    partial void OnIsExportingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CancelCommand));
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressValue));
        CancelFullScanExportCommand.NotifyCanExecuteChanged();
        ExportFullScanCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        BrowseCommand.NotifyCanExecuteChanged();
        RescanCancelledCommand.NotifyCanExecuteChanged();
        Marks.DeleteMarkedCommand.NotifyCanExecuteChanged();
        Duplicates.NotifyScanStateChanged();
    }

    partial void OnExportProgressPercentChanged(int value)
    {
        if (IsExporting)
        {
            OnPropertyChanged(nameof(IsIndeterminate));
            OnPropertyChanged(nameof(ProgressValue));
        }
    }

    partial void OnExportProgressTextChanged(string value)
    {
        if (IsExporting)
        {
            StatusCaption = string.IsNullOrEmpty(value)
                ? "Сохранение полного скана"
                : $"Сохранение полного скана: {value}";
        }
    }
}
