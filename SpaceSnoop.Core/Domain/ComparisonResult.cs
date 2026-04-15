namespace SpaceSnoop.Core.Domain;

public sealed class ComparisonResult(string leftPath, string rightPath, DirectoryComparison root)
{
    public string LeftPath { get; } = leftPath;
    public string RightPath { get; } = rightPath;
    public DirectoryComparison Root { get; } = root;

    public Dictionary<ComparisonStatus, int> GetStatistics()
    {
        var stats = new Dictionary<ComparisonStatus, int>();

        foreach (var status in Enum.GetValues<ComparisonStatus>())
        {
            stats[status] = 0;
        }

        CountRecursive(Root, stats);
        return stats;
    }

    public void ApplyMode(SyncMode mode)
    {
        ApplyModeRecursive(Root, mode);
    }

    public bool HasUnresolvedConflicts()
    {
        return HasUnresolvedConflictsRecursive(Root);
    }

    public bool HasPendingResolution()
    {
        return HasPendingResolutionRecursive(Root);
    }

    public int ResolveAllConflicts(SyncAction action)
    {
        return ResolveAllConflictsRecursive(Root, action);
    }

    private static bool HasPendingResolutionRecursive(DirectoryComparison dir)
    {
        return dir.Files.Any(x => x.Status != ComparisonStatus.Identical && x.Action == SyncAction.None)
               || dir.SubDirectories.Any(HasPendingResolutionRecursive);
    }

    private static int ResolveAllConflictsRecursive(DirectoryComparison dir, SyncAction action)
    {
        var count = 0;

        foreach (var file in dir.Files)
        {
            if (file.Status != ComparisonStatus.Conflict
                && (file.Status is not (ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly)
                    || file.Action != SyncAction.None))
            {
                continue;
            }

            file.Action = action;
            count++;
        }

        count += dir.SubDirectories.Sum(x => ResolveAllConflictsRecursive(x, action));

        return count;
    }

    private static void CountRecursive(DirectoryComparison dir, Dictionary<ComparisonStatus, int> stats)
    {
        foreach (var file in dir.Files)
        {
            stats[file.Status]++;
        }

        foreach (var sub in dir.SubDirectories)
        {
            CountRecursive(sub, stats);
        }
    }

    private static void ApplyModeRecursive(DirectoryComparison dir, SyncMode mode)
    {
        foreach (var file in dir.Files)
        {
            file.Action = mode switch
            {
                SyncMode.LeftToRight => ApplyLeftToRight(file),
                SyncMode.RightToLeft => ApplyRightToLeft(file),
                SyncMode.Bidirectional => ApplyBidirectional(file),
                _ => SyncAction.Skip,
            };
        }

        foreach (var sub in dir.SubDirectories)
        {
            ApplyModeRecursive(sub, mode);
        }
    }

    private static SyncAction ApplyLeftToRight(FileComparison file)
    {
        return file.Status switch
        {
            ComparisonStatus.LeftOnly => SyncAction.CopyToRight,
            ComparisonStatus.Modified => SyncAction.CopyToRight,
            _ => SyncAction.Skip,
        };
    }

    private static SyncAction ApplyRightToLeft(FileComparison file)
    {
        return file.Status switch
        {
            ComparisonStatus.RightOnly => SyncAction.CopyToLeft,
            ComparisonStatus.Modified => SyncAction.CopyToLeft,
            _ => SyncAction.Skip,
        };
    }

    private static SyncAction ApplyBidirectional(FileComparison file)
    {
        switch (file.Status)
        {
            case ComparisonStatus.Modified:
                if (file.LeftModified > file.RightModified)
                {
                    return SyncAction.CopyToRight;
                }

                if (file.RightModified > file.LeftModified)
                {
                    return SyncAction.CopyToLeft;
                }

                file.Status = ComparisonStatus.Conflict;
                return SyncAction.None;

            case ComparisonStatus.LeftOnly:
            case ComparisonStatus.RightOnly:
                return SyncAction.None;

            default:
                return SyncAction.Skip;
        }
    }

    private static bool HasUnresolvedConflictsRecursive(DirectoryComparison dir)
    {
        return dir.Files.Any(x => x.Status == ComparisonStatus.Conflict && x.Action == SyncAction.None)
               || dir.SubDirectories.Any(HasUnresolvedConflictsRecursive);
    }
}
