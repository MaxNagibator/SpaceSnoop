using ModelContextProtocol;
using SpaceSnoop.Core.Cleanup;

namespace SpaceSnoop.Wpf.Mcp;

internal sealed class McpCleanupTools(
    ISettingsStore settings,
    CleanupService cleanup,
    Func<TimeSpan, IReadOnlyList<CleanupTarget>> catalogSource,
    McpPreferences preferences,
    ICleanupAutomation automation,
    McpNavigator navigator,
    ToastNotifier notifier,
    ILogger logger)
{
    private const string RunTool = "cleanup_run";

    public async Task<string> ScanAsync(string[]? targets, CancellationToken cancellationToken)
    {
        var wanted = Normalize(targets);

        logger.McpToolInvoked("cleanup_scan", wanted.Count == 0 ? "все цели" : string.Join(", ", wanted));

        var minAgeHours = MinAgeHours();
        var catalog = catalogSource(TimeSpan.FromHours(minAgeHours));
        var selected = Select(catalog, wanted, targets is not null);
        var measured = await MeasureAllAsync(selected, cancellationToken).ConfigureAwait(false);
        var reclaimable = measured.Where(static item => item.Cleanable).ToList();

        return McpFormat.Serialize(new McpCleanupReport(minAgeHours,
            measured.Sum(static item => item.Bytes),
            SizeFormatter.Format(measured.Sum(static item => item.Bytes)),
            measured.Sum(static item => item.Files),
            reclaimable.Sum(static item => item.Bytes),
            SizeFormatter.Format(reclaimable.Sum(static item => item.Bytes)),
            measured));
    }

    public async Task<string> RunAsync(string[]? targets, bool dryRun, CancellationToken cancellationToken)
    {
        var wanted = Normalize(targets);

        logger.McpToolInvoked(RunTool, $"{string.Join(", ", wanted)}, {(dryRun ? "план" : "запуск")}");

        var minAgeHours = MinAgeHours();
        var catalog = catalogSource(TimeSpan.FromHours(minAgeHours));

        if (wanted.Count == 0)
        {
            throw new McpException($"Ни одна цель очистки не названа. Доступны: {Ids(catalog)}.");
        }

        var selected = Select(catalog, wanted, true);

        if (!dryRun)
        {
            McpGuards.RequireMutations(preferences, logger, RunTool);
            McpDispatch.Run(EnsureIdle);
        }

        var measured = await MeasureAllAsync(selected, cancellationToken).ConfigureAwait(false);
        var ready = measured.Where(static item => item.Cleanable).ToList();
        var plannedBytes = ready.Sum(static item => item.Bytes);
        var plannedFiles = ready.Sum(static item => item.Files);

        if (dryRun)
        {
            return McpFormat.Serialize(new McpCleanupRun(true,
                CleanupConsent.None,
                ready.Count == 0
                    ? "Очищать нечего: названные корзины пусты или недоступны."
                    : "План очистки. Запуск требует dryRun=false и подтверждения человеком в окне приложения.",
                minAgeHours,
                plannedBytes,
                SizeFormatter.Format(plannedBytes),
                plannedFiles,
                0,
                SizeFormatter.Format(0),
                0,
                0,
                false,
                measured));
        }

        if (ready.Count == 0)
        {
            throw new McpException("Очищать нечего: названные корзины пусты или недоступны.");
        }

        List<string> ids = [.. ready.Select(static item => item.Id)];
        var outcome = await McpDispatch.Run(() => StartRun(ids, cancellationToken)).ConfigureAwait(false);

        if (outcome.Consent != CleanupConsent.Granted)
        {
            logger.McpToolRejected(RunTool, outcome.StatusText);

            throw new McpException(outcome.StatusText);
        }

        if (outcome.Failed)
        {
            throw new McpException(outcome.StatusText);
        }

        return McpFormat.Serialize(new McpCleanupRun(false,
            outcome.Consent,
            outcome.StatusText,
            minAgeHours,
            plannedBytes,
            SizeFormatter.Format(plannedBytes),
            plannedFiles,
            outcome.FreedBytes,
            SizeFormatter.Format(outcome.FreedBytes),
            outcome.Deleted,
            outcome.Skipped,
            outcome.Cancelled,
            measured));
    }

    private void EnsureIdle()
    {
        if (automation.IsBusy)
        {
            logger.McpToolRejected(RunTool, "страница занята операцией");

            throw new McpException("Страница «Очистка» сейчас занята другой операцией.");
        }

        if (automation.IsModalBusy)
        {
            logger.McpToolRejected(RunTool, "окно занято диалогом");

            throw new McpException("В окне приложения открыт другой диалог – подтверждение показать нельзя.");
        }
    }

    private Task<CleanupOutcome> StartRun(IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        EnsureIdle();

        navigator.DeferOrNavigate(SectionKey.Cleanup);
        notifier.Notify("Агент просит подтвердить очистку", StatusSeverity.Warning);

        return automation.CleanFromAutomationAsync(ids, cancellationToken);
    }

    private int MinAgeHours()
    {
        return Math.Clamp(settings.GetInt(SettingsKeys.CleanupMinAgeHours, AppDefaults.CleanupMinAgeHoursDefault),
            AppDefaults.CleanupMinAgeHoursMin,
            AppDefaults.CleanupMinAgeHoursMax);
    }

    private async Task<List<McpCleanupTarget>> MeasureAllAsync(List<CleanupTarget> selected, CancellationToken cancellationToken)
    {
        List<McpCleanupTarget> measured = [];

        foreach (var target in selected)
        {
            measured.Add(await MeasureAsync(target, cancellationToken).ConfigureAwait(false));
        }

        return measured;
    }

    private static List<string> Normalize(string[]? targets)
    {
        if (targets is null)
        {
            return [];
        }

        List<string> wanted = [];

        foreach (var target in targets)
        {
            if (target is null)
            {
                continue;
            }

            var id = target.Trim();

            if (id.Length > 0 && !wanted.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                wanted.Add(id);
            }
        }

        return wanted;
    }

    private static List<CleanupTarget> Select(IReadOnlyList<CleanupTarget> catalog, List<string> wanted, bool requested)
    {
        if (wanted.Count == 0)
        {
            return requested
                ? throw new McpException($"Ни одна цель очистки не названа. Доступны: {Ids(catalog)}.")
                : [.. catalog];
        }

        List<CleanupTarget> selected = [];

        foreach (var id in wanted)
        {
            var target = catalog.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? throw new McpException($"Цель очистки «{id}» неизвестна. Доступны: {Ids(catalog)}.");

            selected.Add(target);
        }

        return selected;
    }

    private static string Ids(IReadOnlyList<CleanupTarget> catalog)
    {
        return string.Join(", ", catalog.Select(static item => item.Id));
    }

    private async Task<McpCleanupTarget> MeasureAsync(CleanupTarget target, CancellationToken cancellationToken)
    {
        try
        {
            var measurement = await cleanup.MeasureAsync(target, cancellationToken).ConfigureAwait(false);

            return Describe(target, measurement, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.CleanupMeasureFailed(exception, target.Id);

            return Describe(target, default, exception.Message);
        }
    }

    private static McpCleanupTarget Describe(CleanupTarget target, CleanupMeasurement measurement, string? error)
    {
        var unreadable = measurement.Unreadable ?? [];
        var reported = unreadable.Take(AppDefaults.McpEntryLimitDefault).ToList();

        return new(target.Id,
            target.Name,
            target.Description,
            target.Kind,
            target.Path,
            measurement.Availability,
            CleanupText.Availability(measurement.Availability),
            measurement.Availability == CleanupAvailability.Available && measurement.Files > 0,
            measurement.Bytes,
            SizeFormatter.Format(measurement.Bytes),
            measurement.Files,
            reported,
            unreadable.Count - reported.Count,
            error);
    }
}
