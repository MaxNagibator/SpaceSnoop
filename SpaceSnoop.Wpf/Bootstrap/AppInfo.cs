using System.Reflection;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class AppInfo
{
    public const string Name = "SpaceSnoop";
#if NET11_0_OR_GREATER
    public const string ReleaseAssetProduct = Name + "-Net11";
#else
    public const string ReleaseAssetProduct = Name;
#endif
    public const string RepoSlug = "MaxNagibator/SpaceSnoop";
    public const string RepositoryUrl = "https://github.com/" + RepoSlug;
    public const string ReleasesUrl = RepositoryUrl + "/releases";

    public const string LogFilePrefix = "wpf-";
    public const string LogFileGlob = LogFilePrefix + "*.log";

    public const string SessionStartMarker = Name + ".Wpf запускается";

    public const string SyncArgument = "--sync";
    public const string GalleryArgument = "--gallery";

    public const string DeletionLogFileName = "deleted.txt";
    public const string SyncLogFileName = "sync-log.txt";

    public static string InformationalVersion { get; } = ResolveInformationalVersion();

    public static string Version { get; } = BuildStamp.Trim(InformationalVersion);

    private static string ResolveInformationalVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return string.IsNullOrWhiteSpace(informational)
            ? assembly.GetName().Version?.ToString() ?? "–"
            : informational;
    }
}
