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
        var tally = new PlanTally();
        CountPlannedRecursive(Root, tally);

        return new(tally.NewCopies, tally.ModifiedCopies, tally.Deletes, tally.DirCopies, tally.DirDeletes)
        {
            NewCopyBytes = tally.NewCopyBytes,
            ModifiedCopyBytes = tally.ModifiedCopyBytes,
            CopyToLeftBytes = tally.CopyToLeftBytes,
            CopyToRightBytes = tally.CopyToRightBytes,
            OverwriteLeftBytes = tally.OverwriteLeftBytes,
            OverwriteRightBytes = tally.OverwriteRightBytes,
            DeleteFileBytes = tally.DeleteFileBytes,
            DeleteDirBytes = tally.DeleteDirBytes,
        };
    }

    private static void CountPlannedRecursive(DirectoryComparison dir, PlanTally tally)
    {
        foreach (var file in dir.Files)
        {
            AddFilePlan(file, tally);
        }

        foreach (var sub in dir.SubDirectories)
        {
            AddDirectoryPlan(sub, tally);
        }
    }

    private static void AddFilePlan(FileComparison file, PlanTally tally)
    {
        if (file.Action is SyncAction.CopyToRight or SyncAction.CopyToLeft)
        {
            tally.AddCopy(file);
        }
        else if (file.Action is SyncAction.DeleteLeft or SyncAction.DeleteRight)
        {
            tally.AddDelete(file);
        }
    }

    private static void AddDirectoryPlan(DirectoryComparison dir, PlanTally tally)
    {
        if (dir.Action is SyncAction.DeleteLeft or SyncAction.DeleteRight)
        {
            tally.DirDeletes++;
            AddSubtreeDeleteBytes(dir, dir.Action, tally);
            return;
        }

        if (dir.Action is SyncAction.CopyToRight or SyncAction.CopyToLeft)
        {
            tally.DirCopies++;
        }

        CountPlannedRecursive(dir, tally);
    }

    private static void AddSubtreeDeleteBytes(DirectoryComparison dir, SyncAction action, PlanTally tally)
    {
        foreach (var file in dir.Files)
        {
            tally.AddSubtreeFileBytes(action, file);
        }

        foreach (var sub in dir.SubDirectories)
        {
            AddSubtreeDeleteBytes(sub, action, tally);
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
            ApplyFileMode(file, mode, mirror, winner);
        }

        foreach (var sub in dir.SubDirectories)
        {
            ApplyDirectoryMode(sub, mode, mirror, winner);
        }
    }

    private static void ApplyFileMode(FileComparison file, SyncMode mode, bool mirror, SyncWinner winner)
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

    private static void ApplyDirectoryMode(DirectoryComparison dir, SyncMode mode, bool mirror, SyncWinner winner)
    {
        dir.Action = ApplyDirMode(dir, mode, mirror, winner);
        ApplyModeRecursive(dir, mode, mirror, winner);
    }

    private static SyncAction ApplyDirMode(DirectoryComparison dir, SyncMode mode, bool mirror, SyncWinner winner)
    {
        return dir.Status switch
        {
            ComparisonStatus.LeftOnly => LeftOnlyDirAction(mode, mirror, winner),
            ComparisonStatus.RightOnly => RightOnlyDirAction(mode, mirror, winner),
            _ => SyncAction.None,
        };
    }

    private static SyncAction LeftOnlyDirAction(SyncMode mode, bool mirror, SyncWinner winner)
    {
        return mode switch
        {
            SyncMode.RightToLeft => mirror ? SyncAction.DeleteLeft : SyncAction.Skip,
            SyncMode.Bidirectional => mirror && winner == SyncWinner.Right ? SyncAction.DeleteLeft : SyncAction.CopyToRight,
            _ => SyncAction.CopyToRight,
        };
    }

    private static SyncAction RightOnlyDirAction(SyncMode mode, bool mirror, SyncWinner winner)
    {
        return mode switch
        {
            SyncMode.LeftToRight => mirror ? SyncAction.DeleteRight : SyncAction.Skip,
            SyncMode.Bidirectional => mirror && winner == SyncWinner.Left ? SyncAction.DeleteRight : SyncAction.CopyToLeft,
            _ => SyncAction.CopyToLeft,
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

    private sealed class PlanTally
    {
        public int NewCopies { get; private set; }
        public int ModifiedCopies { get; private set; }
        public int Deletes { get; private set; }
        public int DirCopies { get; set; }
        public int DirDeletes { get; set; }
        public long NewCopyBytes { get; private set; }
        public long ModifiedCopyBytes { get; private set; }
        public long CopyToLeftBytes { get; private set; }
        public long CopyToRightBytes { get; private set; }
        public long OverwriteLeftBytes { get; private set; }
        public long OverwriteRightBytes { get; private set; }
        public long DeleteFileBytes { get; private set; }
        public long DeleteDirBytes { get; private set; }

        public void AddCopy(FileComparison file)
        {
            var isNew = file.Status is ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly;
            var toRight = file.Action == SyncAction.CopyToRight;
            var source = toRight ? file.LeftSize ?? 0 : file.RightSize ?? 0;

            if (isNew)
            {
                NewCopies++;
                NewCopyBytes += source;
            }
            else
            {
                ModifiedCopies++;
                ModifiedCopyBytes += source;
            }

            if (toRight)
            {
                CopyToRightBytes += source;
                OverwriteRightBytes += isNew ? 0 : file.RightSize ?? 0;
            }
            else
            {
                CopyToLeftBytes += source;
                OverwriteLeftBytes += isNew ? 0 : file.LeftSize ?? 0;
            }
        }

        public void AddDelete(FileComparison file)
        {
            Deletes++;
            DeleteFileBytes += SideBytes(file.Action, file);
        }

        public void AddSubtreeFileBytes(SyncAction action, FileComparison file)
        {
            DeleteDirBytes += SideBytes(action, file);
        }

        private static long SideBytes(SyncAction action, FileComparison file)
        {
            return action == SyncAction.DeleteLeft ? file.LeftSize ?? 0 : file.RightSize ?? 0;
        }
    }
}

public sealed record PlannedActions(int NewCopies, int ModifiedCopies, int Deletes, int DirCopies, int DirDeletes)
{
    public static PlannedActions Empty { get; } = new(0, 0, 0, 0, 0);

    public long NewCopyBytes { get; init; }

    public long ModifiedCopyBytes { get; init; }

    public long CopyToLeftBytes { get; init; }

    public long CopyToRightBytes { get; init; }

    public long OverwriteLeftBytes { get; init; }

    public long OverwriteRightBytes { get; init; }

    public long DeleteFileBytes { get; init; }

    public long DeleteDirBytes { get; init; }

    public int Copies => NewCopies + ModifiedCopies;

    public int Total => Copies + Deletes + DirCopies + DirDeletes;

    public long CopyBytes => CopyToLeftBytes + CopyToRightBytes;

    public long DeleteBytes => DeleteFileBytes + DeleteDirBytes;

    public long RequiredLeftBytes => CopyToLeftBytes - OverwriteLeftBytes;

    public long RequiredRightBytes => CopyToRightBytes - OverwriteRightBytes;
}
