using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

internal static class SyncPlanNarrative
{
    internal static NewerSide CombineNewer(NewerSide freshness, bool gitInSync, int gitNewerSign)
    {
        if (gitInSync)
        {
            return NewerSide.Tie;
        }

        return gitNewerSign switch
        {
            < 0 => NewerSide.Left,
            > 0 => NewerSide.Right,
            _ => freshness,
        };
    }

    internal static (Dictionary<ComparisonStatus, int> Files, Dictionary<ComparisonStatus, int> Dirs) CountRemaining(
        DirectoryComparison root,
        IReadOnlyDictionary<object, SyncOutcome> outcomes)
    {
        var files = NewZeroStats();
        var dirs = NewZeroStats();
        WalkRemaining(root, outcomes, files, dirs);
        return (files, dirs);
    }

    internal static List<ConfirmLine> BuildPlanLines(
        PlannedActions planned,
        string? direction,
        IReadOnlyList<PlanReceiver> receivers,
        bool bothWays = false)
    {
        var lines = new List<ConfirmLine>();

        if (direction is not null)
        {
            lines.Add(new ConfirmTextLine($"Направление: {direction}."));
            lines.Add(new ConfirmGapLine());
        }

        if (planned.Total == 0)
        {
            lines.Add(new ConfirmTextLine("Изменений нет."));
            return lines;
        }

        if (planned.Copies > 0)
        {
            lines.Add(new ConfirmMetricLine(
                "Скопировать файлов",
                $"{planned.Copies:N0}",
                SizeFormatter.Format(planned.CopyBytes)));

            if (planned.ModifiedCopies > 0)
            {
                lines.Add(new ConfirmMetricLine(
                    "новых",
                    $"{planned.NewCopies:N0}",
                    SizeFormatter.Format(planned.NewCopyBytes),
                    ConfirmMetricTone.Sub));

                lines.Add(new ConfirmMetricLine(
                    "изменённых",
                    $"{planned.ModifiedCopies:N0}",
                    SizeFormatter.Format(planned.ModifiedCopyBytes),
                    ConfirmMetricTone.Sub));
            }
        }

        if (planned.DirCopies > 0)
        {
            lines.Add(new ConfirmMetricLine("Создать каталогов", $"{planned.DirCopies:N0}", string.Empty));
        }

        if (planned.Deletes > 0)
        {
            lines.Add(new ConfirmMetricLine(
                "Удалить файлов в корзину",
                $"{planned.Deletes:N0}",
                SizeFormatter.Format(planned.DeleteFileBytes),
                ConfirmMetricTone.Danger));
        }

        if (planned.DirDeletes > 0)
        {
            lines.Add(new ConfirmMetricLine(
                "Удалить каталогов целиком",
                $"{planned.DirDeletes:N0}",
                SizeFormatter.Format(planned.DeleteDirBytes),
                ConfirmMetricTone.Danger));

            var whole = planned.DirDeletes == 1
                ? "Каталог уходит в корзину со всем содержимым"
                : "Каталоги уходят в корзину со всем содержимым";

            lines.Add(new ConfirmTextLine(
                planned.Deletes > 0
                    ? $"{whole} – эти файлы в число {planned.Deletes:N0} не входят."
                    : $"{whole}.",
                ConfirmTextTone.Muted));
        }

        var space = DescribeReceivers(receivers, bothWays);

        if (space.Count > 0)
        {
            lines.Add(new ConfirmGapLine());
            lines.AddRange(space);
        }

        if (planned.DeleteBytes > 0)
        {
            lines.Add(new ConfirmGapLine());
            lines.Add(new ConfirmTextLine(
                "Удалённое уходит в корзину – место освободится после её очистки.",
                ConfirmTextTone.Muted));
        }

        return lines;
    }

    internal static string DescribePlanVolume(PlannedActions planned)
    {
        var parts = new List<string>();

        if (planned.CopyBytes > 0)
        {
            parts.Add($"копирование ≈{SizeFormatter.Format(planned.CopyBytes)}");
        }

        if (planned.DeleteBytes > 0)
        {
            parts.Add($"в корзину ≈{SizeFormatter.Format(planned.DeleteBytes)}");
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : $"действий: {planned.Total:N0}";
    }

    internal static SyncVerifyState ResolveVerify(bool requested, SyncReport report)
    {
        if (!requested)
        {
            return SyncVerifyState.None;
        }

        return report.Verified ? SyncVerifyState.Completed : SyncVerifyState.Interrupted;
    }

    internal static string DescribeVerify(bool requested, SyncReport report)
    {
        return ResolveVerify(requested, report) switch
        {
            SyncVerifyState.Completed => $", расхождений: {report.Mismatches.Count:N0}",
            SyncVerifyState.Interrupted => $", проверка прервана (расхождений к тому моменту: {report.Mismatches.Count:N0})",
            _ => string.Empty,
        };
    }

    internal static string? DescribeDeletionRecency(ComparisonResult result)
    {
        var (_, newest) = SyncFreshness.DeletionRecency(result.Root);

        return newest is { } when
            ? $"Новейшее из удаляемого изменено {SyncGitViewModel.FormatStamp(when)} ({SyncGitViewModel.FormatAge(when)}) – убедитесь, что зеркалите не более свежую папку."
            : null;
    }

    internal static List<PlanReceiver> BuildReceivers(ComparisonResult? result, PlannedActions planned)
    {
        if (result is null)
        {
            return [];
        }

        var receivers = new List<PlanReceiver>();

        if (planned.RequiredLeftBytes > 0)
        {
            receivers.Add(new(result.LeftPath, planned.RequiredLeftBytes, TryGetFreeSpace(result.LeftPath)));
        }

        if (planned.RequiredRightBytes > 0)
        {
            receivers.Add(new(result.RightPath, planned.RequiredRightBytes, TryGetFreeSpace(result.RightPath)));
        }

        return receivers;
    }

    internal static Dictionary<ComparisonStatus, int> NewZeroStats()
    {
        var stats = new Dictionary<ComparisonStatus, int>();

        foreach (var status in Enum.GetValues<ComparisonStatus>())
        {
            stats[status] = 0;
        }

        return stats;
    }

    private static void WalkRemaining(
        DirectoryComparison dir,
        IReadOnlyDictionary<object, SyncOutcome> outcomes,
        Dictionary<ComparisonStatus, int> files,
        Dictionary<ComparisonStatus, int> dirs)
    {
        foreach (var file in dir.Files)
        {
            CountRemainingFile(file, outcomes, files);
        }

        foreach (var sub in dir.SubDirectories)
        {
            CountRemainingDirectory(sub, outcomes, dirs);
            WalkRemaining(sub, outcomes, files, dirs);
        }
    }

    private static void CountRemainingFile(
        FileComparison file,
        IReadOnlyDictionary<object, SyncOutcome> outcomes,
        Dictionary<ComparisonStatus, int> files)
    {
        if (outcomes.GetValueOrDefault(file) == SyncOutcome.Applied)
        {
            if (file.Action is not (SyncAction.DeleteLeft or SyncAction.DeleteRight))
            {
                files[ComparisonStatus.Identical]++;
            }

            return;
        }

        files[file.Status]++;
    }

    private static void CountRemainingDirectory(
        DirectoryComparison dir,
        IReadOnlyDictionary<object, SyncOutcome> outcomes,
        Dictionary<ComparisonStatus, int> dirs)
    {
        if (outcomes.GetValueOrDefault(dir) == SyncOutcome.Applied)
        {
            if (dir.Action is not (SyncAction.DeleteLeft or SyncAction.DeleteRight))
            {
                dirs[ComparisonStatus.Identical]++;
            }

            return;
        }

        dirs[dir.Status]++;
    }

    private static List<ConfirmLine> DescribeReceivers(IReadOnlyList<PlanReceiver> receivers, bool bothWays)
    {
        var lines = new List<ConfirmLine>();

        foreach (var receiver in receivers)
        {
            if (receiver.Required <= 0)
            {
                continue;
            }

            if (lines.Count > 0)
            {
                lines.Add(new ConfirmGapLine());
            }

            lines.Add(new ConfirmTextLine($"Приёмник {receiver.Path}", ConfirmTextTone.Muted));
            lines.Add(new ConfirmMetricLine("Потребуется", string.Empty, $"≈{SizeFormatter.Format(receiver.Required)}"));

            if (receiver.Free is not { } free)
            {
                lines.Add(new ConfirmMetricLine("Свободно", string.Empty, "неизвестно"));
                continue;
            }

            var enough = free >= receiver.Required;

            lines.Add(new ConfirmMetricLine(
                "Свободно",
                string.Empty,
                SizeFormatter.Format(free),
                enough ? ConfirmMetricTone.None : ConfirmMetricTone.Danger));

            if (!enough)
            {
                lines.Add(new ConfirmTextLine(
                    $"Не хватает ≈{SizeFormatter.Format(receiver.Required - free)}.",
                    ConfirmTextTone.Danger));
            }
        }

        if (bothWays && lines.Count > 0 && receivers.Count(static receiver => receiver.Required > 0) == 1)
        {
            lines.Add(new ConfirmTextLine("Во встречном направлении копирования нет.", ConfirmTextTone.Muted));
        }

        return lines;
    }

    private static long? TryGetFreeSpace(string path)
    {
        // TODO: свободное место на UNC-приёмнике не читается – DriveInfo знает только локальные корни; перейти на GetDiskFreeSpaceEx, когда появятся жалобы на сетевые папки
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));

            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);

            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
