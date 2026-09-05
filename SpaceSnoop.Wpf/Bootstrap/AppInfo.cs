using System.Reflection;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class AppInfo
{
    public const string Name = "SpaceSnoop";
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

    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return assembly.GetName().Version?.ToString() ?? "–";
        }

        var plusIndex = informational.IndexOf('+');
        return plusIndex >= 0 ? informational[..plusIndex] : informational;
    }
}
