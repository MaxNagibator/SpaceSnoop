namespace SpaceSnoop.Wpf.ViewModels.Sync;

public enum SyncOutcome
{
    None = 0,
    Applied = 1,
    Mismatch = 2,
    Failed = 3,
}

public static class SyncOutcomes
{
    public static Dictionary<object, SyncOutcome> Build(
        ComparisonResult result,
        IReadOnlyList<SyncError> errors,
        IReadOnlyList<SyncMismatch> mismatches)
    {
        var failed = new HashSet<string>(errors.Select(static e => e.RelativePath), StringComparer.OrdinalIgnoreCase);
        var mismatched = new HashSet<string>(mismatches.Select(static m => m.RelativePath), StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<object, SyncOutcome>();
        Walk(result.Root, failed, mismatched, map);
        return map;
    }

    private static SyncOutcome Walk(DirectoryComparison dir, HashSet<string> failed, HashSet<string> mismatched, Dictionary<object, SyncOutcome> map)
    {
        var rollup = OutcomeFor(dir.Action, dir.RelativePath, failed, mismatched);

        foreach (var file in dir.Files)
        {
            var outcome = OutcomeFor(file.Action, file.RelativePath, failed, mismatched);

            if (outcome != SyncOutcome.None)
            {
                map[file] = outcome;
                rollup = Max(rollup, outcome);
            }
        }

        foreach (var sub in dir.SubDirectories)
        {
            rollup = Max(rollup, Walk(sub, failed, mismatched, map));
        }

        if (rollup != SyncOutcome.None)
        {
            map[dir] = rollup;
        }

        return rollup;
    }

    private static SyncOutcome OutcomeFor(SyncAction action, string relativePath, HashSet<string> failed, HashSet<string> mismatched)
    {
        if (action is SyncAction.None or SyncAction.Skip)
        {
            return SyncOutcome.None;
        }

        if (failed.Contains(relativePath))
        {
            return SyncOutcome.Failed;
        }

        return mismatched.Contains(relativePath) ? SyncOutcome.Mismatch : SyncOutcome.Applied;
    }

    private static SyncOutcome Max(SyncOutcome a, SyncOutcome b)
    {
        return (SyncOutcome)Math.Max((int)a, (int)b);
    }
}
