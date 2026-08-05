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

    public const int CleanupMinAgeHoursDefault = 24;
    public const int CleanupMinAgeHoursMin = 1;
    public const int CleanupMinAgeHoursMax = 720;

    public const bool WarnIfNotAdminDefault = true;
    public const StartupPage StartupPageDefault = StartupPage.Scan;

    public const bool SyncShowSizesDefault = true;
    public const bool SyncShowModifiedDefault = false;
    public const bool SyncVerifyDefault = true;
    public const int GitHistoryCountDefault = 4;
    public const SyncSortField SyncFlatSortDefault = SyncSortField.Path;
    public const double SyncIndentStep = 16;

    public const bool SyncDiffCollapseDefault = true;
    public const int DiffContextLines = 3;

    public const bool SyncPathSuggestDefault = true;
    public const string SyncGroupFoldersDefault = ".git,bin,obj";
    public const bool SyncRecycleOverwrittenDefault = true;

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
    public const int McpEntryLimitDefault = 100;
    public const int McpDispatchTimeoutSeconds = 30;
    public const int McpShutdownTimeoutSeconds = 3;

    public const int ViewCaptureLimit = 20;
    public const int ViewCaptureNamesHint = 20;
    public const double ViewCaptureScaleDefault = 1;
    public const double ViewCaptureScaleMin = 0.5;
    public const double ViewCaptureScaleMax = 3;
    public const int GalleryWidthDefault = 1280;
    public const int GalleryHeightDefault = 800;
    public const int GallerySizeMin = 480;
    public const int GallerySizeMax = 4096;
    public const int GalleryFrameDelayMs = 200;
    public const int GalleryThemeDelayMs = 700;
    public const int GalleryModalPollMs = 50;
    public const int GalleryModalAttempts = 80;

    public const bool AgentEnabledDefault = true;
    public const bool AgentConsentDefault = false;
    public const bool AgentHistoryVisibleDefault = false;
    public const int AgentHistoryLimit = 50;
    public const string AgentModelDefault = "";
    public const string AgentEffortDefault = "";
    public const int AgentDetectTimeoutSeconds = 10;
    public const AgentBackendKind AgentBackendDefault = AgentBackendKind.Claude;
    public const bool AgentTranscriptDefault = false;
    public const int AgentTranscriptLimit = 20;

    public const bool PerformanceHudDefault = false;
    public const int PerformanceSampleIntervalMs = 500;
    public const int PerformanceWindowSamples = 20;
    public const int PerformanceHistorySamples = 600;
    public const int PerformanceHistoryPointsMin = 1;
    public const int PerformanceHistoryPointsMax = 240;
    public const int PerformanceHistoryPointsDefault = 60;
    public const int PerformanceHistorySecondsMax = PerformanceHistorySamples * PerformanceSampleIntervalMs / 1000;
    public const double PerformanceRateMinSeconds = 0.25;
    public const double PerformanceEtaMaxSeconds = 24 * 60 * 60;
    public const int PerformanceHitchMs = 500;
    public const int PerformanceHitchLogIntervalSeconds = 5;
    public const int PerformanceHitchRowsMax = 20;
    public const double PerformanceFrameSlowMs = 50;
    public const double PerformanceFrameGapMs = 2000;
    public const bool PerformanceChartExpandedDefault = false;
    public const PerformanceChartWindow PerformanceChartWindowDefault = PerformanceChartWindow.Seconds300;
    public const int PerformanceChartRefreshMs = 1000;
    public const double PerformanceChartPanelHeight = 160;
    public const double PerformanceChartPageHeight = 300;
    public const int PerformanceChartDotLimit = 40;
    public const double PerformanceTileMinWidth = 200;
    public const int PerformanceTileColumnsMax = 3;
    public const double ShellBaseFontSize = 14;

    public const string UpdateRepositoryDefault = AppInfo.RepoSlug;
    public const bool UpdateCheckOnStartupDefault = true;
    public const bool UpdateAutoDownloadDefault = false;
    public const long SelfContainedExeThreshold = 50_000_000;
}
