namespace SpaceSnoop.Wpf.Bootstrap;

public static class OverviewPipeline
{
    public static OverviewRunStatus? Classify(SyncProfile profile)
    {
        var left = profile.Left.Trim();
        var right = profile.Right.Trim();

        if (left.Length == 0 || right.Length == 0)
        {
            return OverviewRunStatus.Unavailable;
        }

        if (SyncProfile.SourceMissing(left, right, HeadlessSync.MapMode(profile.Mode)))
        {
            return OverviewRunStatus.Unavailable;
        }

        if (SyncProfile.PathsOverlap(left, right))
        {
            return OverviewRunStatus.Overlap;
        }

        return null;
    }
}
