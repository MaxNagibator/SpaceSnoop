using System.IO;

namespace SpaceSnoop.Wpf.Bootstrap.Storage;

public static class AppStorage
{
    public const string LogsFolderName = "logs";

    private const string PortableMarkerFileName = "portable.flag";

    private const string LegacyAppDataMarkerFileName = "appdata.flag";

    private static readonly string PortableMarkerPath = Path.Combine(AppContext.BaseDirectory, PortableMarkerFileName);

    private static readonly string LegacyAppDataMarkerPath = Path.Combine(AppContext.BaseDirectory, LegacyAppDataMarkerFileName);

    public static bool UseAppData { get; } = Resolve(
        File.Exists(PortableMarkerPath),
        File.Exists(LegacyAppDataMarkerPath),
        File.Exists(Path.Combine(AppContext.BaseDirectory, TomlSettingsFile.PrimaryFileName)));

    public static string PortableDirectory => AppContext.BaseDirectory;

    public static string AppDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name);

    public static string DataDirectory { get; } = EnsureExists(DirectoryFor(UseAppData));

    public static string ShotsDirectory { get; } = Path.Combine(DataDirectory, ViewCapture.FolderName);

    public static string DirectoryFor(bool useAppData)
    {
        return useAppData ? AppDataDirectory : PortableDirectory;
    }

    internal static bool Resolve(bool portableMarker, bool legacyAppDataMarker, bool legacyPortableData)
    {
        if (portableMarker)
        {
            return false;
        }

        if (legacyAppDataMarker)
        {
            return true;
        }

        return !legacyPortableData;
    }

    public static void SetUseAppData(bool useAppData)
    {
        if (useAppData)
        {
            File.Delete(PortableMarkerPath);
        }
        else
        {
            File.WriteAllText(PortableMarkerPath, string.Empty);
        }

        File.Delete(LegacyAppDataMarkerPath);
    }

    public static int Migrate(string source, string destination)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        EnsureExists(destination);

        string[] files =
        [
            TomlSettingsFile.PrimaryFileName,
            AppInfo.DeletionLogFileName,
            ChatHistoryStore.FileName,
        ];

        var copied = new List<string>();

        foreach (var name in files)
        {
            var from = Path.Combine(source, name);

            if (File.Exists(from))
            {
                File.Copy(from, Path.Combine(destination, name), true);
                copied.Add(from);
            }
        }

        foreach (var from in Directory.EnumerateFiles(source, SyncLog.FileGlob))
        {
            File.Copy(from, Path.Combine(destination, Path.GetFileName(from)), true);
            copied.Add(from);
        }

        var logsSource = Path.Combine(source, LogsFolderName);

        if (Directory.Exists(logsSource))
        {
            var logsDestination = EnsureExists(Path.Combine(destination, LogsFolderName));

            foreach (var file in Directory.GetFiles(logsSource))
            {
                File.Copy(file, Path.Combine(logsDestination, Path.GetFileName(file)), true);
                copied.Add(file);
            }
        }

        return copied.Count(static file => !TryDelete(file));
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string EnsureExists(string directory)
    {
        Directory.CreateDirectory(directory);
        return directory;
    }
}
