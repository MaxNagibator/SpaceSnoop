using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.ViewModels.Overview;

internal static class OverviewNarrative
{
    internal static bool HasNetworkDeleteOrOverwrite(CompareDirectoriesRequest request, ComparisonResult? comparison, bool recycleOverwritten)
    {
        return SyncPlanNarrative.TargetsWithoutRecycleBin(
            request.LeftPath,
            request.RightPath,
            request.Mode,
            request.Mirror,
            recycleOverwritten)
            || (comparison is not null && SyncPlanNarrative.HasNetworkDeleteOrOverwrite(comparison, recycleOverwritten));
    }

    internal static string? BuildSyncWarning(IEnumerable<OverviewRowViewModel> rows, bool permanentNetwork)
    {
        var incomplete = DescribeIncomplete(rows);
        var network = permanentNetwork
            ? $"{SyncPlanNarrative.NetworkDeleteWarning}. Перед запуском каталоги будут сравнены заново."
            : null;

        return string.Join(
            ' ',
            new[]
            {
                network, incomplete,
            }.OfType<string>()) is { Length: > 0 } warning
            ? warning
            : null;
    }

    internal static string FormatUncError(Exception exception, string left, string right)
    {
        if (UncPath.IsUnc(left))
        {
            return NetworkErrorText.Describe(exception, left, AdminElevation.IsElevated);
        }

        return UncPath.IsUnc(right)
            ? NetworkErrorText.Describe(exception, right, AdminElevation.IsElevated)
            : exception.Message;
    }

    internal static (int Synced, int Failed, int Skipped) Tally(OverviewRowViewModel row)
    {
        return row.Status switch
        {
            OverviewRunStatus.Synced when !row.SyncHadErrors => (1, 0, 0),
            OverviewRunStatus.Synced or OverviewRunStatus.Error => (0, 1, 0),
            _ => (0, 0, 1)
        };
    }

    internal static PlannedActions? SumPlans(IEnumerable<OverviewRowViewModel> rows)
    {
        var plans = rows.Select(static row => row.Comparison?.CountPlannedActions()).OfType<PlannedActions>().ToList();

        if (plans.Count == 0)
        {
            return null;
        }

        return plans.Aggregate(
            PlannedActions.Empty,
            static (total, plan) => new(
                total.NewCopies + plan.NewCopies,
                total.ModifiedCopies + plan.ModifiedCopies,
                total.Deletes + plan.Deletes,
                total.DirCopies + plan.DirCopies,
                total.DirDeletes + plan.DirDeletes)
            {
                NewCopyBytes = total.NewCopyBytes + plan.NewCopyBytes,
                ModifiedCopyBytes = total.ModifiedCopyBytes + plan.ModifiedCopyBytes,
                CopyToLeftBytes = total.CopyToLeftBytes + plan.CopyToLeftBytes,
                CopyToRightBytes = total.CopyToRightBytes + plan.CopyToRightBytes,
                OverwriteLeftBytes = total.OverwriteLeftBytes + plan.OverwriteLeftBytes,
                OverwriteRightBytes = total.OverwriteRightBytes + plan.OverwriteRightBytes,
                DeleteFileBytes = total.DeleteFileBytes + plan.DeleteFileBytes,
                DeleteDirBytes = total.DeleteDirBytes + plan.DeleteDirBytes
            });
    }

    internal static (int Compared, int Failed, int Skipped) TallyCompare(IReadOnlyList<OverviewRowViewModel> targets, int allRows)
    {
        var compared = targets.Count(static row => row.Status == OverviewRunStatus.Compared);
        var failed = targets.Count(static row => row.Status == OverviewRunStatus.Error);

        return (compared, failed, allRows - compared - failed);
    }

    internal static (int Synced, int Failed, int Skipped) TallySync(IReadOnlyList<OverviewRowViewModel> targets, int excluded)
    {
        var synced = 0;
        var failed = 0;
        var skipped = excluded;

        foreach (var row in targets)
        {
            var (rowSynced, rowFailed, rowSkipped) = Tally(row);
            synced += rowSynced;
            failed += rowFailed;
            skipped += rowSkipped;
        }

        return (synced, failed, skipped);
    }

    internal static string DescribeCompared((int Compared, int Failed, int Skipped) tally)
    {
        return $"Сравнено пар: {tally.Compared}, ошибок: {tally.Failed}, пропущено: {tally.Skipped}";
    }

    internal static string DescribeSynced((int Synced, int Failed, int Skipped) tally)
    {
        return $"Синхронизировано профилей: {tally.Synced}, c ошибками: {tally.Failed}, пропущено: {tally.Skipped}";
    }

    internal static IReadOnlyList<ConfirmLine> BuildRowLines(OverviewRowViewModel row, PlannedActions? planned, bool permanentNetwork = false)
    {
        List<ConfirmLine> lines =
        [
            new ConfirmTextLine(row.Name, ConfirmTextTone.Strong),
            new ConfirmTextLine($"{row.Left} → {row.Right}", ConfirmTextTone.Muted),
            new ConfirmGapLine()
        ];

        var fallback = permanentNetwork
            ? "Файлы будут скопированы по направлению профиля, удаления на сетевом хранилище – безвозвратно."
            : "Файлы будут скопированы по направлению профиля, удаления – в корзину.";
        AddPlanLines(lines, planned, fallback, permanentNetwork);

        return lines;
    }

    internal static IReadOnlyList<ConfirmLine> BuildBatchLines(int count, int excluded, PlannedActions? planned, bool permanentNetwork = false)
    {
        List<ConfirmLine> lines = [new ConfirmMetricLine("Профилей в пакете", $"{count:N0}", string.Empty)];

        if (excluded > 0)
        {
            lines.Add(new ConfirmMetricLine("Исключено", $"{excluded:N0}", string.Empty, ConfirmMetricTone.Sub));
        }

        lines.Add(new ConfirmGapLine());

        var fallback = permanentNetwork
            ? "Файлы будут скопированы по направлению каждого профиля, удаления на сетевом хранилище – безвозвратно."
            : "Файлы будут скопированы по направлению каждого профиля, удаления – в корзину.";
        AddPlanLines(lines, planned, fallback, permanentNetwork);

        return lines;
    }

    internal static string? DescribeIncomplete(IEnumerable<OverviewRowViewModel> rows)
    {
        var affected = rows
            .Where(static row => row.Comparison?.IncompleteDirectories().Count > 0)
            .Select(static row => row.Name)
            .ToList();

        if (affected.Count == 0)
        {
            return null;
        }

        var tail = affected.Count > 1 ? $" и ещё {Plural.Format(affected.Count - 1, "профиль", "профиля", "профилей")}" : string.Empty;

        return $"Сравнение неполное у профиля «{affected[0]}»{tail}. Удаления в непрочитанных ветках отключены.";
    }

    internal static (string Message, StatusSeverity Severity) DescribeOutcome(string caption, int ok, int failed)
    {
        var severity = failed switch
        {
            > 0 => StatusSeverity.Error,
            _ when ok == 0 => StatusSeverity.Warning,
            _ => StatusSeverity.Success
        };

        return (caption, severity);
    }

    private static void AddPlanLines(List<ConfirmLine> lines, PlannedActions? planned, string fallback, bool permanentNetwork)
    {
        if (planned is null)
        {
            lines.Add(new ConfirmTextLine(fallback));
            return;
        }

        lines.AddRange(SyncPlanNarrative.BuildPlanLines(planned, null, [], permanentNetwork: permanentNetwork));
    }
}
