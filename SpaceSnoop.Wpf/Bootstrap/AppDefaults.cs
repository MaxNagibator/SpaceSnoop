using System.IO.Compression;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class AppDefaults
{
    public const double IntensityDefault = 8;
    public const double IntensityMin = 1;
    public const double IntensityMax = 20;

    public const bool ScanMultithreadingDefault = true;
    public const ScanSortField ScanSortModeDefault = ScanSortField.Size;
    public const bool ScanSortInvertDefault = true;
    public const bool ScanTreemapDefault = false;
    public const bool ScanRevealFilesDefault = false;
    public const int TopChildrenLimit = 7;
    public const int TreemapTileLimit = 150;
    public const int TreemapPreviewLimit = 40;
    public const double TreemapPreviewHeight = 150;

    public const bool DeleteConfirmDefault = true;
    public const DeleteMode DeleteModeDefault = DeleteMode.RecycleBin;

    public const bool ArchiveDeleteOriginalDefault = true;
    public const CompressionLevel ArchiveCompressionDefault = CompressionLevel.Optimal;

    public const bool WarnIfNotAdminDefault = true;
    public const StartupPage StartupPageDefault = StartupPage.Scan;

    public const bool SyncShowSizesDefault = true;
    public const bool SyncShowModifiedDefault = false;
    public const bool SyncVerifyDefault = true;
    public const int GitHistoryCountDefault = 4;
    public const SyncFlatSortField SyncFlatSortDefault = SyncFlatSortField.Path;
    public const double SyncIndentStep = 16;

    public const bool SyncDiffCollapseDefault = true;
    public const int DiffContextLines = 3;

    public const bool SyncPathSuggestDefault = true;
    public const string SyncGroupFoldersDefault = ".git,bin,obj";

    public const long SyncLogFileSizeLimitBytes = 5_000_000;
    public const int SyncLogRetainedFileCount = 5;

    public const OverviewSortField OverviewSortDefault = OverviewSortField.None;
    public const bool OverviewGroupUnchangedDefault = true;

    public const bool McpEnabledDefault = false;
    public const bool McpAllowMutationsDefault = false;
    public const int McpPortDefault = 7654;
    public const int McpPortMin = 1024;
    public const int McpPortMax = 65535;
    public const string McpEndpointPath = "/mcp";
    public const int McpEntryLimitMin = 1;
    public const int McpEntryLimitMax = 10_000;
    public const int McpDispatchTimeoutSeconds = 30;

    public const bool AgentEnabledDefault = true;
    public const bool AgentConsentDefault = false;
    public const string AgentModelDefault = "";
    public const int AgentDetectTimeoutSeconds = 10;

    public const string UpdateRepositoryDefault = AppInfo.RepoSlug;
    public const bool UpdateCheckOnStartupDefault = true;
    public const bool UpdateAutoDownloadDefault = false;
    public const long SelfContainedExeThreshold = 50_000_000;
}
