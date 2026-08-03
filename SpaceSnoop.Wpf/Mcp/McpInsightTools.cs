using System.IO;

namespace SpaceSnoop.Wpf.Mcp;

internal sealed class McpInsightTools(
    ISettingsStore settings,
    McpPreferences preferences,
    DockerService docker,
    PerformanceMonitor performance,
    McpNavigator navigator,
    McpStateReader state,
    ILogger logger)
{
    public string GetState()
    {
        logger.McpToolInvoked("get_app_state", "-");

        return McpDispatch.Run(() => McpFormat.Serialize(new McpAppState(AppInfo.Name,
            AppInfo.Version,
            AdminElevation.IsElevated,
            navigator.CurrentSectionKey,
            preferences.AllowMutations,
            SyncProfileStore.Load(settings).Count,
            state.ReadScanState(),
            state.ReadSyncState())));
    }

    public string ListProfiles()
    {
        logger.McpToolInvoked("list_profiles", "-");

        var profiles = SyncProfileStore.Load(settings)
            .Select(static profile => new McpProfile(profile.Id,
                profile.Name,
                profile.Left,
                profile.Right,
                HeadlessSync.MapMode(profile.Mode),
                profile.Winner,
                profile.Mirror,
                profile.Exclusions,
                profile.Enabled,
                profile.SkipInBatch,
                $"{profile.Interval} {profile.Time}",
                McpFormat.DescribeUnavailable(profile)))
            .ToList();

        return McpFormat.Serialize(profiles);
    }

    public string ListDrives()
    {
        logger.McpToolInvoked("list_drives", "-");

        var drives = DriveInfo.GetDrives().Select(McpFormat.DescribeDrive).ToList();

        return McpFormat.Serialize(drives);
    }

    public string GetPerformance(int historySeconds, int historyPoints)
    {
        logger.McpToolInvoked("get_performance", historySeconds > 0 ? $"история за {historySeconds} с, точек до {historyPoints}" : "-");

        var snapshot = performance.Snapshot;

        return McpFormat.Serialize(new McpPerformance(performance.IsRunning,
            snapshot.CapturedAtUtc,
            Math.Round(McpFormat.SnapshotAge(snapshot).TotalMilliseconds),
            McpFormat.DescribeWindow(snapshot),
            snapshot.SampleCount,
            Math.Round(snapshot.ObservedSpanSeconds, 1),
            Math.Round(snapshot.UiDelayMs, 1),
            Math.Round(snapshot.UiPeakMs, 1),
            Math.Round(snapshot.UiAverageMs, 1),
            snapshot.ManagedBytes,
            SizeFormatter.Format(snapshot.ManagedBytes),
            snapshot.WorkingSetBytes,
            SizeFormatter.Format(snapshot.WorkingSetBytes),
            snapshot.WorkingSetPeakBytes,
            SizeFormatter.Format(snapshot.WorkingSetPeakBytes),
            snapshot.Gen0Collections,
            snapshot.Gen1Collections,
            snapshot.Gen2Collections,
            Math.Round(snapshot.StartupSeconds, 2),
            Math.Round(snapshot.FrameLastMs, 1),
            Math.Round(snapshot.FramePeakMs, 1),
            Math.Round(snapshot.FrameAverageMs, 1),
            snapshot.FrameCount,
            snapshot.SlowFrameCount,
            McpFormat.DescribeOperation(snapshot.Operation),
            ReadHistory(historySeconds, historyPoints)));
    }

    public async Task<string> GetDockerUsageAsync(bool includeObjects, int entryLimit, CancellationToken cancellationToken)
    {
        entryLimit = McpGuards.ClampEntryLimit(entryLimit);

        logger.McpToolInvoked("docker_usage", $"объекты {includeObjects}, записей до {entryLimit}");

        var snapshot = await docker.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        var report = await BuildDockerReportAsync(snapshot, includeObjects, entryLimit, cancellationToken).ConfigureAwait(false);
        return McpFormat.Serialize(report);
    }

    private McpPerformanceHistory? ReadHistory(int historySeconds, int historyPoints)
    {
        var seconds = McpGuards.ClampHistorySeconds(historySeconds);

        if (seconds == 0)
        {
            return null;
        }

        var points = McpGuards.ClampHistoryPoints(historyPoints);

        return McpFormat.DescribeHistory(performance.CaptureHistory(TimeSpan.FromSeconds(seconds), points), seconds, points);
    }

    private async Task<McpDockerReport> BuildDockerReportAsync(
        DockerSnapshot snapshot,
        bool includeObjects,
        int entryLimit,
        CancellationToken cancellationToken)
    {
        var buckets = snapshot.Buckets
            .Select(static bucket => new McpDockerBucket(bucket.Type,
                bucket.TotalCount,
                bucket.Active,
                bucket.Size,
                DockerSize.ToBytes(bucket.Size),
                bucket.Reclaimable,
                DockerSize.ToBytes(bucket.Reclaimable)))
            .ToList();

        if (!snapshot.Available || !includeObjects)
        {
            return new(snapshot.Available, snapshot.Error, buckets, null, 0);
        }

        var inventory = await docker.GetInventoryAsync(cancellationToken).ConfigureAwait(false);
        var objects = inventory
            .OrderByDescending(static item => item.SizeBytes)
            .Take(entryLimit)
            .Select(static item => new McpDockerObject(item.Kind, item.Id, item.Name, item.Size, item.SizeBytes, item.InUse, item.Detail))
            .ToList();

        return new(true, snapshot.Error, buckets, objects, Math.Max(0, inventory.Count - objects.Count));
    }
}
