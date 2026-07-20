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

    public Dictionary<ComparisonStatus, int> GetDirectoryStatistics()
    {
        var stats = new Dictionary<ComparisonStatus, int>();

        foreach (var status in Enum.GetValues<ComparisonStatus>())
        {
            stats[status] = 0;
        }

        CountDirectoriesRecursive(Root, stats);
        return stats;
    }

    public void ApplyMode(SyncMode mode, bool mirror = false, SyncWinner winner = SyncWinner.Newest)
    {
        ApplyModeRecursive(Root, mode, mirror, winner);
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

    public PlannedActions CountPlannedActions()
    {
        var newCopies = 0;
        var modifiedCopies = 0;
        var deletes = 0;
        var dirCopies = 0;
        var dirDeletes = 0;
        CountPlannedRecursive(Root, ref newCopies, ref modifiedCopies, ref deletes, ref dirCopies, ref dirDeletes);
        return new(newCopies, modifiedCopies, deletes, dirCopies, dirDeletes);
    }

    private static void CountPlannedRecursive(
        DirectoryComparison dir,
        ref int newCopies,
        ref int modifiedCopies,
        ref int deletes,
        ref int dirCopies,
        ref int dirDeletes)
    {
        foreach (var file in dir.Files)
        {
            switch (file.Action)
            {
                case SyncAction.CopyToRight or SyncAction.CopyToLeft:
                    if (file.Status is ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly)
                    {
                        newCopies++;
                    }
                    else
                    {
                        modifiedCopies++;
                    }

                    break;

                case SyncAction.DeleteLeft or SyncAction.DeleteRight:
                    deletes++;
                    break;
            }
        }

        foreach (var sub in dir.SubDirectories)
        {
            if (sub.Action is SyncAction.DeleteLeft or SyncAction.DeleteRight)
            {
                dirDeletes++;
                continue;
            }

            if (sub.Action is SyncAction.CopyToRight or SyncAction.CopyToLeft)
            {
                dirCopies++;
            }

            CountPlannedRecursive(sub, ref newCopies, ref modifiedCopies, ref deletes, ref dirCopies, ref dirDeletes);
        }
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

    private static void CountDirectoriesRecursive(DirectoryComparison dir, Dictionary<ComparisonStatus, int> stats)
    {
        foreach (var sub in dir.SubDirectories)
        {
            stats[sub.Status]++;
            CountDirectoriesRecursive(sub, stats);
        }
    }

    private static void ApplyModeRecursive(DirectoryComparison dir, SyncMode mode, bool mirror, SyncWinner winner)
    {
        foreach (var file in dir.Files)
        {
            if (file.Status == ComparisonStatus.Conflict)
            {
                file.Status = ComparisonStatus.Modified;
            }

            file.Action = mode switch
            {
                SyncMode.LeftToRight => ApplyLeftToRight(file, mirror),
                SyncMode.RightToLeft => ApplyRightToLeft(file, mirror),
                SyncMode.Bidirectional => ApplyBidirectional(file, mirror, winner),
                _ => SyncAction.Skip,
            };
        }

        foreach (var sub in dir.SubDirectories)
        {
            sub.Action = ApplyDirMode(sub, mode, mirror, winner);
            ApplyModeRecursive(sub, mode, mirror, winner);
        }
    }

    private static SyncAction ApplyDirMode(DirectoryComparison dir, SyncMode mode, bool mirror, SyncWinner winner)
    {
        return dir.Status switch
        {
            ComparisonStatus.LeftOnly => mode switch
            {
                SyncMode.RightToLeft => mirror ? SyncAction.DeleteLeft : SyncAction.Skip,
                SyncMode.Bidirectional => mirror && winner == SyncWinner.Right ? SyncAction.DeleteLeft : SyncAction.CopyToRight,
                _ => SyncAction.CopyToRight,
            },
            ComparisonStatus.RightOnly => mode switch
            {
                SyncMode.LeftToRight => mirror ? SyncAction.DeleteRight : SyncAction.Skip,
                SyncMode.Bidirectional => mirror && winner == SyncWinner.Left ? SyncAction.DeleteRight : SyncAction.CopyToLeft,
                _ => SyncAction.CopyToLeft,
            },
            _ => SyncAction.None,
        };
    }

    private static SyncAction ApplyLeftToRight(FileComparison file, bool mirror)
    {
        return file.Status switch
        {
            ComparisonStatus.LeftOnly => SyncAction.CopyToRight,
            ComparisonStatus.Modified => SyncAction.CopyToRight,
            ComparisonStatus.RightOnly when mirror => SyncAction.DeleteRight,
            _ => SyncAction.Skip,
        };
    }

    private static SyncAction ApplyRightToLeft(FileComparison file, bool mirror)
    {
        return file.Status switch
        {
            ComparisonStatus.RightOnly => SyncAction.CopyToLeft,
            ComparisonStatus.Modified => SyncAction.CopyToLeft,
            ComparisonStatus.LeftOnly when mirror => SyncAction.DeleteLeft,
            _ => SyncAction.Skip,
        };
    }

    private static SyncAction ApplyBidirectional(FileComparison file, bool mirror, SyncWinner winner)
    {
        switch (file.Status)
        {
            case ComparisonStatus.Modified:
                return winner switch
                {
                    SyncWinner.Left => SyncAction.CopyToRight,
                    SyncWinner.Right => SyncAction.CopyToLeft,
                    _ => ResolveModifiedByNewest(file),
                };

            case ComparisonStatus.LeftOnly:
                return mirror && winner == SyncWinner.Right ? SyncAction.DeleteLeft : SyncAction.CopyToRight;

            case ComparisonStatus.RightOnly:
                return mirror && winner == SyncWinner.Left ? SyncAction.DeleteRight : SyncAction.CopyToLeft;

            default:
                return SyncAction.Skip;
        }
    }

    private static SyncAction ResolveModifiedByNewest(FileComparison file)
    {
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
    }

    private static bool HasUnresolvedConflictsRecursive(DirectoryComparison dir)
    {
        return dir.Files.Any(x => x.Status == ComparisonStatus.Conflict && x.Action == SyncAction.None)
               || dir.SubDirectories.Any(HasUnresolvedConflictsRecursive);
    }
}

public sealed record PlannedActions(int NewCopies, int ModifiedCopies, int Deletes, int DirCopies, int DirDeletes)
{
    public int Copies => NewCopies + ModifiedCopies;

    public int Total => Copies + Deletes + DirCopies + DirDeletes;
}
