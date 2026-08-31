namespace SpaceSnoop.Wpf.Bootstrap.Storage;

public static class SettingsKeys
{
    public const string ShowPageHeader = "wpf.shell.show_page_header";
    public const string FontScale = "wpf.shell.font_scale";
    public const string EnableToastNotifications = "wpf.notifications.toast";
    public const string StartupPage = "wpf.shell.startup_page";
    public const string WarnIfNotAdmin = "wpf.startup.admin_warning";
    public const string LastPage = "wpf.shell.last_page";
    public const string NavCollapsed = "wpf.shell.nav_collapsed";
    public const string SettingsSection = "wpf.settings.section";
    public const string PerformanceHud = "wpf.perf.hud";
    public const string PerformanceChart = "wpf.perf.chart";
    public const string PerformanceChartWindow = "wpf.perf.chart_window";

    public const string WindowLeft = "wpf.window.left";
    public const string WindowTop = "wpf.window.top";
    public const string WindowWidth = "wpf.window.width";
    public const string WindowHeight = "wpf.window.height";
    public const string WindowMaximized = "wpf.window.maximized";

    public const string ScanMultithreading = "wpf.scan.multithreading";
    public const string ScanParallelism = "wpf.scan.parallelism";
    public const string ScanMediaAware = "wpf.scan.media_aware";
    public const string ScanIntensity = "wpf.scan.intensity";
    public const string ScanLastDrive = "wpf.scan.last_drive";
    public const string ScanSortMode = "wpf.scan.sort_mode";
    public const string ScanSortInvert = "wpf.scan.sort_invert";
    public const string ScanTreemap = "wpf.scan.treemap";
    public const string ScanRevealFiles = "wpf.scan.reveal_files";

    public const string ScanView = "wpf.scan.view";

    public const string ScanDuplicatesEnabled = "wpf.scan.duplicates.enabled";
    public const string ScanMftEnabled = "wpf.scan.mft.enabled";
    public const string ScanMftRootOnly = "wpf.scan.mft.root_only";
    public const string ScanDuplicatesMinSize = "wpf.scan.duplicates.min_size_mb";

    public const string ScanInspectorCollapsed = "wpf.scan.inspector.collapsed";
    public const string ScanInspectorWidth = "wpf.scan.inspector.width";

    public const string DeleteConfirm = "wpf.delete.confirm";
    public const string DeleteMode = "wpf.delete.mode";
    public const string DefaultExclusions = "wpf.exclusions.default";

    public const string ArchiveDeleteOriginal = "wpf.archive.delete_original";
    public const string ArchiveCompression = "wpf.archive.compression";

    public const string CleanupMinAgeHours = "wpf.cleanup.min_age_hours";
    public const string CleanupSelected = "wpf.cleanup.selected";

    public const string SyncLeft = "wpf.sync.left";
    public const string SyncRight = "wpf.sync.right";
    public const string SyncExclusions = "wpf.sync.exclusions";
    public const string SyncMode = "wpf.sync.mode";
    public const string SyncMirror = "wpf.sync.mirror";
    public const string SyncWinner = "wpf.sync.winner";
    public const string SyncShowIdentical = "wpf.sync.show_identical";
    public const string SyncShowSizes = "wpf.sync.show_sizes";
    public const string SyncShowModified = "wpf.sync.show_modified";
    public const string SyncBlankAbsent = "wpf.sync.blank_absent";
    public const string SyncVerify = "wpf.sync.verify";
    public const string SyncHideApplied = "wpf.sync.hide_applied";
    public const string SyncFlatView = "wpf.sync.flat_view";
    public const string SyncFlatSort = "wpf.sync.flat_sort";
    public const string SyncFlatSortDesc = "wpf.sync.flat_sort_desc";
    public const string SyncDiffUnified = "wpf.sync.diff_unified";
    public const string SyncDiffCollapse = "wpf.sync.diff_collapse";
    public const string SyncPathSuggest = "wpf.sync.path_suggest";
    public const string SyncGitFolders = "wpf.sync.git_folders";
    public const string SyncGitHistoryCount = "wpf.sync.git_history_count";
    public const string SyncGroupFolders = "wpf.sync.group_folders";
    public const string SyncRecycleOverwritten = "wpf.sync.recycle_overwritten";

    public const string OverviewSort = "wpf.overview.sort";
    public const string OverviewSortDesc = "wpf.overview.sort_desc";
    public const string OverviewGroupUnchanged = "wpf.overview.group_unchanged";

    public const string ScheduleProfiles = "wpf.schedule.profiles";

    public const string BatchSource = "wpf.batch.source";
    public const string BatchDest = "wpf.batch.dest";
    public const string BatchMode = "wpf.batch.mode";
    public const string BatchMirror = "wpf.batch.mirror";
    public const string BatchWinner = "wpf.batch.winner";
    public const string BatchExclusions = "wpf.batch.exclusions";
    public const string BatchSort = "wpf.batch.sort";

    public const string McpEnabled = "wpf.mcp.enabled";
    public const string McpPort = "wpf.mcp.port";
    public const string McpToken = "wpf.mcp.token";
    public const string McpAllowMutations = "wpf.mcp.allow_mutations";

    public const string AgentEnabled = "wpf.agent.enabled";
    public const string AgentBackend = "wpf.agent.backend";
    public const string AgentHistoryVisible = $"{AgentPrefix}.history_visible";
    public const string AgentTranscript = $"{AgentPrefix}.transcript";
    public const string AgentModelShared = $"{AgentPrefix}.model";
    public const string AgentCliPathShared = $"{AgentPrefix}.cli_path";
    public const string AgentConsentShared = $"{AgentPrefix}.consent";

    private const string AgentPrefix = "wpf.agent";

    public const string UpdateDismissedVersion = "wpf.update.dismissed";
    public const string UpdateRepository = "wpf.update.repository";
    public const string UpdateCheckOnStartup = "wpf.update.check";
    public const string UpdateAutoDownload = "wpf.update.auto_download";

    public static string Theme => ThemeManager.SettingsKeyName;

    public static string AgentModel(AgentBackendKind backend)
    {
        return $"{AgentPrefix}.{Suffix(backend)}.model";
    }

    public static string AgentCliPath(AgentBackendKind backend)
    {
        return $"{AgentPrefix}.{Suffix(backend)}.cli_path";
    }

    public static string AgentEffort(AgentBackendKind backend)
    {
        return $"{AgentPrefix}.{Suffix(backend)}.effort";
    }

    public static string AgentConsent(AgentBackendKind backend)
    {
        return $"{AgentPrefix}.{Suffix(backend)}.consent";
    }

    private static string Suffix(AgentBackendKind backend)
    {
        return backend.ToString().ToLowerInvariant();
    }
}
