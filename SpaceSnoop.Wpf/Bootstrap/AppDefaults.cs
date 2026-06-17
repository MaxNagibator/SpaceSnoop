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
    public const int TopChildrenLimit = 7;
    public const int TreemapTileLimit = 150;

    public const bool DeleteConfirmDefault = true;
    public const DeleteMode DeleteModeDefault = DeleteMode.RecycleBin;

    public const bool WarnIfNotAdminDefault = true;
    public const StartupPage StartupPageDefault = StartupPage.Scan;
    public const BackdropKind BackdropDefault = BackdropKind.None;

    public const bool SyncShowSizesDefault = true;
    public const double SyncIndentStep = 16;
}
