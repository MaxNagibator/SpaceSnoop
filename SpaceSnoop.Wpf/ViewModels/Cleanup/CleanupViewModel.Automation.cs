namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class CleanupViewModel
{
    bool ICleanupAutomation.IsBusy => IsBusy;

    bool ICleanupAutomation.IsModalBusy => _modals.HasActive;

    async Task<CleanupOutcome> ICleanupAutomation.CleanFromAutomationAsync(
        IReadOnlyList<string> targetIds,
        CancellationToken cancellationToken)
    {
        var rows = SelectForAutomation(targetIds);

        if (await MeasureForAutomationAsync(rows, cancellationToken))
        {
            rows = SelectForAutomation(targetIds);

            if (await MeasureForAutomationAsync(rows, cancellationToken))
            {
                return new(CleanupConsent.Stale, 0, 0, 0, false, false,
                    "Порог возраста файлов меняли во время подготовки – очистки не было, повторите вызов.");
            }
        }

        var ready = rows.Where(static row => row.CanClean).ToList();

        if (ready.Count == 0)
        {
            return new(CleanupConsent.Nothing, 0, 0, 0, false, false, "Очищать нечего: названные корзины пусты или недоступны.");
        }

        var bytes = ready.Sum(static row => row.SizeBytes);
        var files = ready.Sum(static row => row.Files);
        var confirm = BuildConfirm(ready, bytes, files, "Очистку запросил агент через MCP.");

        _logger.CleanupRunRequested(ready.Count, bytes);

        var consent = await ConfirmForAutomationAsync(confirm, cancellationToken);

        if (consent != CleanupConsent.Granted)
        {
            _logger.CleanupRunDeclined(consent.ToString());

            return new(consent, 0, 0, 0, false, false, consent switch
            {
                CleanupConsent.Declined => "Человек отказал в очистке.",
                CleanupConsent.Busy => "В окне приложения открыт другой диалог – подтверждение показать нельзя.",
                _ => "Человек не ответил на подтверждение вовремя.",
            });
        }

        cancellationToken.ThrowIfCancellationRequested();

        var dialog = await RunConfirmedAsync(ready, bytes, files, cancellationToken, true);

        return new(CleanupConsent.Granted,
            dialog.FreedBytes,
            dialog.Deleted,
            dialog.Skipped,
            dialog.WasCancelled,
            dialog.HasFailure,
            dialog.StatusText);
    }

    private List<CleanupTargetViewModel> SelectForAutomation(IReadOnlyList<string> targetIds)
    {
        return Targets
            .Where(row => targetIds.Contains(row.Model.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<bool> MeasureForAutomationAsync(IReadOnlyList<CleanupTargetViewModel> rows, CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await MeasureRowAsync(row, cancellationToken);
            }
        }
        finally
        {
            IsBusy = false;
        }

        return ApplyPendingRebuild();
    }

    private async Task RunDialogForAutomationAsync(CleanupProgressDialogViewModel dialog)
    {
        var show = _dialogs.ShowAsync(dialog);
        var run = dialog.StartCommand.ExecuteAsync(null);

        if (await Task.WhenAny(show, run) == show)
        {
            dialog.RequestStop();
        }

        await run;

        dialog.CloseCommand.Execute(null);

        await show;
    }

    private async Task<CleanupConsent> ConfirmForAutomationAsync(ConfirmDialogViewModel confirm, CancellationToken cancellationToken)
    {
        if (_modals.HasActive)
        {
            return CleanupConsent.Busy;
        }

        var show = _dialogs.ShowAsync(confirm);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(AppDefaults.McpConfirmTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        var waiting = Task.Delay(Timeout.Infinite, linked.Token);
        bool closedByUs;
        bool accepted;

        try
        {
            closedByUs = await Task.WhenAny(show, waiting) != show;

            if (closedByUs)
            {
                confirm.ChooseCommand.Execute(confirm.Choices.First(static choice => choice.IsDismissive));
            }

            accepted = await show;
        }
        finally
        {
            await linked.CancelAsync();

            try
            {
                await waiting;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (!closedByUs)
        {
            return accepted ? CleanupConsent.Granted : CleanupConsent.Declined;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return CleanupConsent.TimedOut;
    }
}
