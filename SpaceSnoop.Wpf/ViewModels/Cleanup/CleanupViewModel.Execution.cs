using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class CleanupViewModel
{
    [RelayCommand]
    private void OpenFolder(CleanupTargetViewModel? row)
    {
        if (row is not { HasPath: true, Path.Length: > 0 })
        {
            return;
        }

        if (!_shell.Open(row.Path))
        {
            _notifier.Notify($"Не удалось открыть «{row.Path}».", StatusSeverity.Warning);
        }
    }

    [RelayCommand]
    private void OpenSystemTool(CleanupTargetViewModel? row)
    {
        if (row is not { IsUnsupported: true })
        {
            return;
        }

        var drive = Path.GetPathRoot(row.Path)?.TrimEnd('\\');

        var tool = Path.Combine(Environment.SystemDirectory, "cleanmgr.exe");

        if (string.IsNullOrEmpty(drive) || !_shell.Start(tool, "/d", drive))
        {
            _notifier.Notify("Не удалось запустить «Очистку диска» Windows.", StatusSeverity.Warning);
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in Targets)
        {
            row.IsSelected = row.CanClean;
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in Targets)
        {
            row.IsSelected = false;
        }
    }

    [RelayCommand]
    private Task CleanOneAsync(CleanupTargetViewModel? row)
    {
        return row is null || !row.CanClean || IsBusy
            ? Task.CompletedTask
            : RunCleanupAsync([row]);
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private Task CleanSelectedAsync()
    {
        return RunCleanupAsync(SelectedRows());
    }

    private async Task RunCleanupAsync(IReadOnlyList<CleanupTargetViewModel> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var bytes = rows.Sum(static x => x.SizeBytes);
        var files = rows.Sum(static x => x.Files);
        var confirm = CleanupConfirm.Build(rows, bytes, files, null);

        if (!await _dialogs.ShowAsync(confirm))
        {
            return;
        }

        await RunConfirmedAsync(rows, bytes, files, CancellationToken.None, false);
    }

    private async Task<CleanupProgressDialogViewModel> RunConfirmedAsync(
        IReadOnlyList<CleanupTargetViewModel> rows,
        long bytes,
        int files,
        CancellationToken cancellationToken,
        bool automated)
    {
        var dialog = _dialogFactory.Create(new([.. rows.Select(static x => x.Model)], bytes, files, cancellationToken));

        IsBusy = true;

        try
        {
            if (automated)
            {
                await RunDialogForAutomationAsync(dialog);
            }
            else
            {
                await _dialogs.ShowAsync(dialog);
            }
        }
        finally
        {
            await dialog.StopAsync();
            IsBusy = false;
        }

        _uiDispatcher.Invoke(() => _notifier.Notify(
            dialog.StatusText,
            dialog.HasErrors || dialog.WasCancelled || dialog.FreedBytes == 0 ? StatusSeverity.Warning : StatusSeverity.Success));

        await RemeasureAfterCleanupAsync(rows, dialog.StatusText, cancellationToken);

        return dialog;
    }

    private async Task RemeasureAfterCleanupAsync(
        IReadOnlyList<CleanupTargetViewModel> rows,
        string summary,
        CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _measureCts = cancellation;
        ProgressValue = 0;
        ProgressMax = Math.Max(rows.Count, 1);
        IsBusy = true;
        StatusText = summary;

        try
        {
            foreach (var row in rows)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                StatusText = $"{summary} Повторный замер {ProgressValue + 1} из {ProgressMax}: {row.Name}";
                await MeasureRowAsync(row, cancellation.Token);
                ProgressValue++;
            }

            StatusText = summary;
        }
        catch (OperationCanceledException)
        {
            StatusText = $"{summary} Повторный замер остановлен.";
        }
        finally
        {
            _measureCts = null;
            IsBusy = false;
            ApplyPendingRebuild();
        }
    }
}
