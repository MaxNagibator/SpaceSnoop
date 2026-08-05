namespace SpaceSnoop.Wpf.ViewModels.Sync;

internal static class SyncActionCycles
{
    private static readonly SyncAction[] LeftOnlyActions = [SyncAction.CopyToRight, SyncAction.Skip, SyncAction.DeleteLeft];
    private static readonly SyncAction[] RightOnlyActions = [SyncAction.CopyToLeft, SyncAction.Skip, SyncAction.DeleteRight];
    private static readonly SyncAction[] LeftOnlyActionsNoDelete = [SyncAction.CopyToRight, SyncAction.Skip];
    private static readonly SyncAction[] RightOnlyActionsNoDelete = [SyncAction.CopyToLeft, SyncAction.Skip];
    private static readonly SyncAction[] BothSidesActions = [SyncAction.CopyToRight, SyncAction.CopyToLeft, SyncAction.Skip];

    private static readonly SyncAction[] OneSidedLeftCycle = [SyncAction.CopyToRight, SyncAction.Skip];
    private static readonly SyncAction[] OneSidedRightCycle = [SyncAction.CopyToLeft, SyncAction.Skip];

    internal static SyncAction[] Directory { get; } =
    [
        SyncAction.CopyToRight,
        SyncAction.CopyToLeft,
        SyncAction.Skip,
    ];

    internal static SyncAction[] ForFile(ComparisonStatus status, bool deleteAllowed = true)
    {
        return status switch
        {
            ComparisonStatus.LeftOnly => deleteAllowed ? LeftOnlyActions : LeftOnlyActionsNoDelete,
            ComparisonStatus.RightOnly => deleteAllowed ? RightOnlyActions : RightOnlyActionsNoDelete,
            _ => BothSidesActions,
        };
    }

    internal static SyncAction[] ForOneSidedDirectory(ComparisonStatus status)
    {
        return status == ComparisonStatus.LeftOnly ? OneSidedLeftCycle : OneSidedRightCycle;
    }

    internal static SyncAction Next(SyncAction[] cycle, SyncAction? current)
    {
        var index = current is { } action ? Array.IndexOf(cycle, action) : -1;
        return index < 0 ? cycle[0] : cycle[(index + 1) % cycle.Length];
    }

    internal static (bool Actionable, SyncAction? Uniform) ComputeSubtree(DirectoryComparison root)
    {
        var actionable = false;
        var mixed = false;
        SyncAction? uniform = null;

        foreach (var file in EnumerateFiles(root))
        {
            if (file.Status == ComparisonStatus.Identical)
            {
                continue;
            }

            actionable = true;

            if (uniform is null)
            {
                uniform = file.Action;
            }
            else if (file.Action != uniform)
            {
                mixed = true;
            }
        }

        return (actionable, mixed ? null : uniform);
    }

    private static IEnumerable<FileComparison> EnumerateFiles(DirectoryComparison dir)
    {
        foreach (var file in dir.Files)
        {
            yield return file;
        }

        foreach (var sub in dir.SubDirectories)
        {
            foreach (var file in EnumerateFiles(sub))
            {
                yield return file;
            }
        }
    }
}
