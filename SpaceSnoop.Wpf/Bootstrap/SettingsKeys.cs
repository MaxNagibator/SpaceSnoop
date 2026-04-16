namespace SpaceSnoop.Wpf.Bootstrap;

public static class SettingsKeys
{
    public const string ShowPageHeader = "wpf.shell.show_page_header";
    public const string FontScale = "wpf.shell.font_scale";
    public const string EnableToastNotifications = "wpf.notifications.toast";
    public const string StartupPage = "wpf.shell.startup_page";
    public const string WarnIfNotAdmin = "wpf.startup.admin_warning";
    public const string LastPage = "wpf.shell.last_page";
    public const string NavCollapsed = "wpf.shell.nav_collapsed";
    public const string Backdrop = "wpf.shell.backdrop";

    public const string WindowLeft = "wpf.window.left";
    public const string WindowTop = "wpf.window.top";
    public const string WindowWidth = "wpf.window.width";
    public const string WindowHeight = "wpf.window.height";
    public const string WindowMaximized = "wpf.window.maximized";

    public const string ScanMultithreading = "wpf.scan.multithreading";
    public const string ScanParallelism = "wpf.scan.parallelism";
    public const string ScanIntensity = "wpf.scan.intensity";
    public const string ScanLastDrive = "wpf.scan.last_drive";
    public const string ScanSortMode = "wpf.scan.sort_mode";
    public const string ScanSortInvert = "wpf.scan.sort_invert";

    public const string ScanInspectorCollapsed = "wpf.scan.inspector.collapsed";
    public const string ScanInspectorWidth = "wpf.scan.inspector.width";

    public const string DeleteConfirm = "wpf.delete.confirm";
    public const string DeleteMode = "wpf.delete.mode";
    public const string DefaultExclusions = "wpf.exclusions.default";

    public const string SyncLeft = "wpf.sync.left";
    public const string SyncRight = "wpf.sync.right";
    public const string SyncExclusions = "wpf.sync.exclusions";
    public const string SyncMode = "wpf.sync.mode";
    public const string SyncShowIdentical = "wpf.sync.show_identical";
    public const string SyncShowSizes = "wpf.sync.show_sizes";

    public static string Theme => ThemeManager.SettingsKeyName;
}
