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
    int RightOnly,
    DateTime? LeftChangedMax,
    DateTime? RightChangedMax)
{
    public NewerSide Verdict
    {
        get
        {
            if (LeftChangedMax is not { } left)
            {
                return RightChangedMax is null ? NewerSide.None : NewerSide.Right;
            }

            if (RightChangedMax is not { } right)
            {
                return NewerSide.Left;
            }

            var delta = left - right;

            return delta.Duration() <= DirectoryComparer.FatTimestampTolerance ? NewerSide.Tie
                : delta > TimeSpan.Zero ? NewerSide.Left
                : NewerSide.Right;
        }
    }

    public double LeadSeconds => (LeftChangedMax, RightChangedMax) switch
    {
        ({ } left, { } right) => Math.Abs((left - right).TotalSeconds),
        (null, null) => 0,
        _ => double.MaxValue,
    };
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
        DateTime? leftChangedMax = null;
        DateTime? rightChangedMax = null;
        var tolerance = DirectoryComparer.FatTimestampTolerance;

        void Walk(DirectoryComparison dir)
        {
            foreach (var file in dir.Files)
            {
                Bump(ref leftMax, file.LeftModified);
                Bump(ref rightMax, file.RightModified);

                switch (file.Status)
                {
                    case ComparisonStatus.LeftOnly:
                        leftOnly++;
                        Bump(ref leftChangedMax, file.LeftModified);
                        break;

                    case ComparisonStatus.RightOnly:
                        rightOnly++;
                        Bump(ref rightChangedMax, file.RightModified);
                        break;

                    case ComparisonStatus.Modified or ComparisonStatus.Conflict:
                        Bump(ref leftChangedMax, file.LeftModified);
                        Bump(ref rightChangedMax, file.RightModified);

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
        return new(leftNewer, rightNewer, leftMax, rightMax, leftOnly, rightOnly, leftChangedMax, rightChangedMax);

        static void Bump(ref DateTime? max, DateTime? value)
        {
            if (value is { } v && (max is null || v > max))
            {
                max = v;
            }
        }
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
