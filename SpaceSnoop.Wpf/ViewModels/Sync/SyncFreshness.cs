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

            if (delta.Duration() <= DirectoryComparer.FatTimestampTolerance)
            {
                return NewerSide.Tie;
            }

            return delta > TimeSpan.Zero ? NewerSide.Left : NewerSide.Right;
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
        var accumulator = new FreshnessAccumulator(DirectoryComparer.FatTimestampTolerance);
        accumulator.Walk(root);
        return accumulator.Build();
    }

    internal static (int Count, DateTime? Newest) DeletionRecency(DirectoryComparison root)
    {
        var accumulator = new DeletionAccumulator();
        accumulator.Walk(root);
        return accumulator.Result;
    }

    private sealed class FreshnessAccumulator(TimeSpan tolerance)
    {
        private int _leftNewer;
        private int _rightNewer;
        private DateTime? _leftMax;
        private DateTime? _rightMax;
        private int _leftOnly;
        private int _rightOnly;
        private DateTime? _leftChangedMax;
        private DateTime? _rightChangedMax;

        public void Walk(DirectoryComparison dir)
        {
            foreach (var file in dir.Files)
            {
                AddFile(file);
            }

            foreach (var sub in dir.SubDirectories)
            {
                Walk(sub);
            }
        }

        public FreshnessSummary Build()
        {
            return new(_leftNewer, _rightNewer, _leftMax, _rightMax, _leftOnly, _rightOnly, _leftChangedMax, _rightChangedMax);
        }

        private static void Bump(ref DateTime? max, DateTime? value)
        {
            if (value is { } current && (max is null || current > max))
            {
                max = current;
            }
        }

        private void AddFile(FileComparison file)
        {
            Bump(ref _leftMax, file.LeftModified);
            Bump(ref _rightMax, file.RightModified);

            switch (file.Status)
            {
                case ComparisonStatus.LeftOnly:
                    _leftOnly++;
                    Bump(ref _leftChangedMax, file.LeftModified);
                    break;

                case ComparisonStatus.RightOnly:
                    _rightOnly++;
                    Bump(ref _rightChangedMax, file.RightModified);
                    break;

                case ComparisonStatus.Modified or ComparisonStatus.Conflict:
                    AddChangedFile(file);
                    break;
            }
        }

        private void AddChangedFile(FileComparison file)
        {
            Bump(ref _leftChangedMax, file.LeftModified);
            Bump(ref _rightChangedMax, file.RightModified);

            if (file.LeftModified is { } left && file.RightModified is { } right)
            {
                CountNewer(left - right);
            }
        }

        private void CountNewer(TimeSpan delta)
        {
            if (delta.Duration() <= tolerance)
            {
                return;
            }

            if (delta > TimeSpan.Zero)
            {
                _leftNewer++;
            }
            else
            {
                _rightNewer++;
            }
        }
    }

    private sealed class DeletionAccumulator
    {
        private int _count;
        private DateTime? _newest;

        public (int Count, DateTime? Newest) Result => (_count, _newest);

        public void Walk(DirectoryComparison dir)
        {
            foreach (var file in dir.Files)
            {
                Consider(file.Action, file.LeftModified, file.RightModified);
            }

            foreach (var sub in dir.SubDirectories)
            {
                if (sub.Action is SyncAction.DeleteLeft or SyncAction.DeleteRight)
                {
                    Consider(sub.Action, sub.LeftModified, sub.RightModified);
                    continue;
                }

                Walk(sub);
            }
        }

        private void Consider(SyncAction action, DateTime? left, DateTime? right)
        {
            if (action is not (SyncAction.DeleteLeft or SyncAction.DeleteRight))
            {
                return;
            }

            _count++;

            var when = action switch
            {
                SyncAction.DeleteLeft => left,
                SyncAction.DeleteRight => right,
                _ => null,
            };

            if (when is not { } value || _newest is not null && value <= _newest)
            {
                return;
            }

            _newest = value;
        }
    }
}
