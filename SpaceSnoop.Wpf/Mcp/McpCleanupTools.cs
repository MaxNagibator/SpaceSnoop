using ModelContextProtocol;
using SpaceSnoop.Core.Cleanup;

namespace SpaceSnoop.Wpf.Mcp;

internal sealed class McpCleanupTools(ISettingsStore settings, CleanupService cleanup, ILogger logger)
{
    public async Task<string> ScanAsync(string[]? targets, CancellationToken cancellationToken)
    {
        var wanted = Normalize(targets);

        logger.McpToolInvoked("cleanup_scan", wanted.Count == 0 ? "все цели" : string.Join(", ", wanted));

        var minAgeHours = Math.Clamp(settings.GetInt(SettingsKeys.CleanupMinAgeHours, AppDefaults.CleanupMinAgeHoursDefault),
            AppDefaults.CleanupMinAgeHoursMin,
            AppDefaults.CleanupMinAgeHoursMax);

        var catalog = CleanupCatalog.BuildDefault(TimeSpan.FromHours(minAgeHours));
        var selected = Select(catalog, wanted, targets is not null);

        List<McpCleanupTarget> measured = [];

        foreach (var target in selected)
        {
            measured.Add(await MeasureAsync(target, cancellationToken).ConfigureAwait(false));
        }

        var reclaimable = measured.Where(static item => item.Cleanable).ToList();

        return McpFormat.Serialize(new McpCleanupReport(minAgeHours,
            measured.Sum(static item => item.Bytes),
            SizeFormatter.Format(measured.Sum(static item => item.Bytes)),
            measured.Sum(static item => item.Files),
            reclaimable.Sum(static item => item.Bytes),
            SizeFormatter.Format(reclaimable.Sum(static item => item.Bytes)),
            measured));
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
