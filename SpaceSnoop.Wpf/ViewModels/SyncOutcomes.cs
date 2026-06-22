namespace SpaceSnoop.Wpf.ViewModels;

public enum SyncOutcome
{
    None = 0,
    Applied = 1,
    Failed = 2,
}

public static class SyncOutcomes
{
    public static Dictionary<object, SyncOutcome> Build(ComparisonResult result, IReadOnlyList<SyncError> errors)
    {
        var failed = new HashSet<string>(errors.Select(static e => e.RelativePath), StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<object, SyncOutcome>();
        Walk(result.Root, failed, map);
        return map;
    }

    private static SyncOutcome Walk(DirectoryComparison dir, HashSet<string> failed, Dictionary<object, SyncOutcome> map)
    {
        var rollup = OutcomeFor(dir.Action, dir.RelativePath, failed);

        foreach (var file in dir.Files)
        {
            var outcome = OutcomeFor(file.Action, file.RelativePath, failed);

            if (outcome != SyncOutcome.None)
            {
                map[file] = outcome;
                rollup = Max(rollup, outcome);
            }
        }

        foreach (var sub in dir.SubDirectories)
        {
            rollup = Max(rollup, Walk(sub, failed, map));
        }

        if (rollup != SyncOutcome.None)
        {
            map[dir] = rollup;
        }

        return rollup;
    }

    private static SyncOutcome OutcomeFor(SyncAction action, string relativePath, HashSet<string> failed)
    {
        if (action is SyncAction.None or SyncAction.Skip)
        {
            return SyncOutcome.None;
        }

        return failed.Contains(relativePath) ? SyncOutcome.Failed : SyncOutcome.Applied;
    }

    private static SyncOutcome Max(SyncOutcome a, SyncOutcome b)
    {
        return (SyncOutcome)Math.Max((int)a, (int)b);
    }
}
