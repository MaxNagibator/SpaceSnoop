namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public readonly record struct CleanupTally(long TotalBytes, long SelectedBytes, int SelectedFiles, int SelectedCount);

public static class CleanupTotals
{
    public static CleanupTally Compute(IReadOnlyList<CleanupTargetViewModel> rows)
    {
        long total = 0;
        long selected = 0;
        var count = 0;
        var files = 0;

        foreach (var row in rows)
        {
            if (!row.IsAvailable)
            {
                continue;
            }

            total += row.SizeBytes;

            if (!row.IsSelected)
            {
                continue;
            }

            selected += row.SizeBytes;
            files += row.Files;
            count++;
        }

        return new(total, selected, files, count);
    }

    public static void ApplyShares(IReadOnlyList<CleanupTargetViewModel> rows, long total)
    {
        foreach (var row in rows)
        {
            row.Share = total > 0 && row.IsAvailable ? (double)row.SizeBytes / total : 0;
        }
    }
}
