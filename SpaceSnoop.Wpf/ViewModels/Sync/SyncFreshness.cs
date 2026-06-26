namespace SpaceSnoop.Wpf.ViewModels.Sync;

internal enum NewerSide
{
    None = 0,
    Left = 1,
    Right = 2,
    Tie = 3,
}

internal readonly record struct FreshnessSummary(
    int LeftNewer,
    int RightNewer,
    DateTime? LeftMax,
    DateTime? RightMax,
    int LeftOnly,
    int RightOnly)
{
    public NewerSide Verdict =>
        LeftNewer == 0 && RightNewer == 0 ? NewerSide.None
        : LeftNewer > RightNewer ? NewerSide.Left
        : RightNewer > LeftNewer ? NewerSide.Right
        : NewerSide.Tie;
}

internal static class SyncFreshness
{
    internal static FreshnessSummary Compute(DirectoryComparison root)
    {
        var leftNewer = 0;
        var rightNewer = 0;
        DateTime? leftMax = null;
        DateTime? rightMax = null;
        var leftOnly = 0;
        var rightOnly = 0;
        var tolerance = DirectoryComparer.FatTimestampTolerance;

        void Walk(DirectoryComparison dir)
        {
            foreach (var file in dir.Files)
            {
                if (file.LeftModified is { } left && (leftMax is null || left > leftMax))
                {
                    leftMax = left;
                }

                if (file.RightModified is { } right && (rightMax is null || right > rightMax))
                {
                    rightMax = right;
                }

                switch (file.Status)
                {
                    case ComparisonStatus.LeftOnly:
                        leftOnly++;
                        break;

                    case ComparisonStatus.RightOnly:
                        rightOnly++;
                        break;

                    case ComparisonStatus.Modified or ComparisonStatus.Conflict:
                        if (file.LeftModified is { } l && file.RightModified is { } r)
                        {
                            var delta = l - r;

                            if (delta.Duration() > tolerance)
                            {
                                if (delta > TimeSpan.Zero)
                                {
                                    leftNewer++;
                                }
                                else
                                {
                                    rightNewer++;
                                }
                            }
                        }

                        break;
                }
            }

            foreach (var sub in dir.SubDirectories)
            {
                Walk(sub);
            }
        }

        Walk(root);
        return new(leftNewer, rightNewer, leftMax, rightMax, leftOnly, rightOnly);
    }

    internal static (int Count, DateTime? Newest) DeletionRecency(DirectoryComparison root)
    {
        var count = 0;
        DateTime? newest = null;

        void Consider(DateTime? when)
        {
            count++;

            if (when is { } value && (newest is null || value > newest))
            {
                newest = value;
            }
        }

        void Walk(DirectoryComparison dir)
        {
            foreach (var file in dir.Files)
            {
                if (file.Action == SyncAction.DeleteRight)
                {
                    Consider(file.RightModified);
                }
                else if (file.Action == SyncAction.DeleteLeft)
                {
                    Consider(file.LeftModified);
                }
            }

            foreach (var sub in dir.SubDirectories)
            {
                if (sub.Action == SyncAction.DeleteRight)
                {
                    Consider(sub.RightModified);
                    continue;
                }

                if (sub.Action == SyncAction.DeleteLeft)
                {
                    Consider(sub.LeftModified);
                    continue;
                }

                Walk(sub);
            }
        }

        Walk(root);
        return (count, newest);
    }
}
