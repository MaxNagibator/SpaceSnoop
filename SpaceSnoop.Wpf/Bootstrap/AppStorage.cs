using System.IO;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class AppStorage
{
    public const string LogsFolderName = "logs";

    private const string MarkerFileName = "appdata.flag";

    private static readonly string MarkerPath = Path.Combine(AppContext.BaseDirectory, MarkerFileName);

    public static bool UseAppData { get; } = File.Exists(MarkerPath);

    public static string PortableDirectory => AppContext.BaseDirectory;

    public static string AppDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name);

    public static string DataDirectory { get; } = EnsureExists(DirectoryFor(UseAppData));

    public static string DirectoryFor(bool useAppData)
    {
        return useAppData ? AppDataDirectory : PortableDirectory;
    }

    public static void SetUseAppData(bool useAppData)
    {
        if (useAppData)
        {
            File.WriteAllText(MarkerPath, string.Empty);
        }
        else if (File.Exists(MarkerPath))
        {
            File.Delete(MarkerPath);
        }
    }

    public static void Migrate(string source, string destination)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        EnsureExists(destination);

        string[] files =
        [
            TomlSettingsFile.PrimaryFileName,
            AppInfo.DeletionLogFileName,
            AppInfo.SyncLogFileName,
        ];

        foreach (var name in files)
        {
            var from = Path.Combine(source, name);

            if (File.Exists(from))
            {
                File.Copy(from, Path.Combine(destination, name), true);
            }
        }

        var logsSource = Path.Combine(source, LogsFolderName);

        if (!Directory.Exists(logsSource))
        {
            return;
        }

        var logsDestination = EnsureExists(Path.Combine(destination, LogsFolderName));

        foreach (var file in Directory.GetFiles(logsSource))
        {
            File.Copy(file, Path.Combine(logsDestination, Path.GetFileName(file)), true);
        }
    }

    private static string EnsureExists(string directory)
    {
        Directory.CreateDirectory(directory);
        return directory;
    }
}
