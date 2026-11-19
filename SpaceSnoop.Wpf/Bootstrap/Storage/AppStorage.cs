using System.IO;
using System.Reflection;
using Tomlyn;
using Tomlyn.Model;

namespace SpaceSnoop.Wpf.Bootstrap.Storage;

public static class AppStorage
{
    public const string LogsFolderName = "logs";

    internal const string StorageModeKey = "storage";

    internal const string PortableModeValue = "portable";

    internal const string AppDataModeValue = "appdata";

    internal const string AppDataDirectoriesKey = "appdata_directories";

    private const string OwnSettingsKeyPrefix = "wpf.";

    private const string PortableBuildMetadataKey = "PortableBuild";

    private static readonly string ExecutableDirectory = ResolveExecutableDirectory(Environment.ProcessPath, AppContext.BaseDirectory);

    public static string AppDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name);

    public static bool IsPortableBuild { get; } = ReadPortableBuildMetadata();

    public static bool UseAppData { get; } = Resolve(ExecutableDirectory, AppDataDirectory, IsPortableBuild);

    public static string PortableDirectory => ExecutableDirectory;

    public static string DataDirectory { get; } = EnsureExists(DirectoryFor(UseAppData));

    public static string ShotsDirectory { get; } = Path.Combine(DataDirectory, ViewCapture.FolderName);

    public static string DirectoryFor(bool useAppData)
    {
        return useAppData ? AppDataDirectory : PortableDirectory;
    }

    public static bool HasSettings(string directory)
    {
        return HasProfileSettings(Path.Combine(directory, TomlSettingsFile.PrimaryFileName))
            || File.Exists(Path.Combine(directory, TomlSettingsFile.LegacyFileName));
    }

    public static void SetUseAppData(bool useAppData, bool migrated = false)
    {
        SetUseAppData(ExecutableDirectory, AppDataDirectory, useAppData, migrated);
    }

    public static int Migrate(string source, string destination)
    {
        if (AreSameDirectory(source, destination))
        {
            return 0;
        }

        EnsureExists(destination);

        var sourceTomlPath = Path.Combine(source, TomlSettingsFile.PrimaryFileName);
        var destinationTomlPath = Path.Combine(destination, TomlSettingsFile.PrimaryFileName);
        var sourceDirectories = ReadDirectoryListFromFile(sourceTomlPath);
        var destinationDirectories = ReadDirectoryListFromFile(destinationTomlPath);

        string[] files =
        [
            TomlSettingsFile.PrimaryFileName,
            TomlSettingsFile.LegacyFileName,
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

        var remaining = 0;

        foreach (var file in copied)
        {
            if (sourceDirectories.Count > 0 && string.Equals(file, sourceTomlPath, StringComparison.OrdinalIgnoreCase))
            {
                var preserved = new TomlTable();
                WriteAppDataDirectories(preserved, sourceDirectories);
                TomlSettingsFile.Save(sourceTomlPath, preserved);
                continue;
            }

            if (!TryDelete(file))
            {
                remaining++;
            }
        }

        if (destinationDirectories.Count > 0 && File.Exists(destinationTomlPath))
        {
            UpdateToml(destinationTomlPath, table => MergeAppDataDirectories(table, destinationDirectories));
        }

        return remaining;
    }

    internal static void SetUseAppData(string executableDirectory, string appDataDirectory, bool useAppData, bool migrated = false)
    {
        var portableTomlPath = Path.Combine(executableDirectory, TomlSettingsFile.PrimaryFileName);
        var appDataTomlPath = Path.Combine(appDataDirectory, TomlSettingsFile.PrimaryFileName);
        var sameDirectory = AreSameDirectory(executableDirectory, appDataDirectory);

        if (useAppData)
        {
            EnsureExists(appDataDirectory);
            UpdateToml(appDataTomlPath, table =>
            {
                TomlSettingsFile.SetString(table, StorageModeKey, AppDataModeValue);
                UpdateAppDataDirectories(table, executableDirectory, include: true);
                return true;
            });

            if (File.Exists(portableTomlPath) && !sameDirectory)
            {
                UpdateToml(portableTomlPath, static table =>
                {
                    TomlSettingsFile.SetString(table, StorageModeKey, AppDataModeValue);
                    table.Remove(AppDataDirectoriesKey);
                    return true;
                });
            }

            return;
        }

        EnsureExists(executableDirectory);
        UpdateToml(portableTomlPath, static table =>
        {
            TomlSettingsFile.SetString(table, StorageModeKey, PortableModeValue);
            table.Remove(AppDataDirectoriesKey);
            return true;
        });

        if (!sameDirectory && File.Exists(appDataTomlPath))
        {
            var table = TomlSettingsFile.Read(appDataTomlPath);
            var changed = UpdateAppDataDirectories(table, executableDirectory, include: false);

            if (changed)
            {
                if (table.Keys.Count == 0 || (IsStorageMetadataOnly(table) && !table.ContainsKey(AppDataDirectoriesKey)))
                {
                    TryDelete(appDataTomlPath);
                }
                else
                {
                    TomlSettingsFile.Save(appDataTomlPath, table);
                }
            }
        }
    }

    internal static string ResolveExecutableDirectory(string? processPath, string baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var host = Path.GetFileNameWithoutExtension(processPath);

            if (!IsSharedHost(host))
            {
                var directory = Path.GetDirectoryName(processPath);

                if (!string.IsNullOrWhiteSpace(directory))
                {
                    return Path.EndsInDirectorySeparator(directory)
                        ? directory
                        : directory + Path.DirectorySeparatorChar;
                }
            }
        }

        return baseDirectory;
    }

    internal static bool Resolve(string executableDirectory, string appDataDirectory, bool isPortableBuild)
    {
        var portableTomlPath = Path.Combine(executableDirectory, TomlSettingsFile.PrimaryFileName);
        var appDataTomlPath = Path.Combine(appDataDirectory, TomlSettingsFile.PrimaryFileName);

        if (File.Exists(portableTomlPath))
        {
            var portableTable = TryParseToml(portableTomlPath);

            if (portableTable is not null)
            {
                var mode = TomlSettingsFile.GetString(portableTable, StorageModeKey);

                if (string.Equals(mode, PortableModeValue, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.Equals(mode, AppDataModeValue, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (IsDirectoryInAppDataOverrides(appDataTomlPath, executableDirectory))
            {
                return true;
            }

            if (!AreSameDirectory(executableDirectory, appDataDirectory))
            {
                return false;
            }
        }

        if (IsDirectoryInAppDataOverrides(appDataTomlPath, executableDirectory))
        {
            return true;
        }

        if (isPortableBuild)
        {
            return false;
        }

        return !HasLegacyPortableSettings(executableDirectory, appDataDirectory);
    }

    internal static bool HasLegacyPortableSettings(string executableDirectory, string appDataDirectory)
    {
        var tomlPath = Path.Combine(executableDirectory, TomlSettingsFile.PrimaryFileName);

        return !HasSettings(appDataDirectory)
            && IsOwnLegacySettings(Path.Combine(executableDirectory, TomlSettingsFile.LegacyFileName))
            && TryMigrateLegacy(tomlPath);
    }

    internal static bool TryNormalizeDirectory(string? raw, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            var full = Path.GetFullPath(raw.Trim());
            normalized = Path.TrimEndingDirectorySeparator(full);
            return !string.IsNullOrEmpty(normalized);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool ReadPortableBuildMetadata()
    {
        var value = typeof(AppStorage).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(static attribute => string.Equals(attribute.Key, PortableBuildMetadataKey, StringComparison.Ordinal))
            ?.Value;

        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasProfileSettings(string tomlPath)
    {
        if (!File.Exists(tomlPath))
        {
            return false;
        }

        var table = TryParseToml(tomlPath);

        return table is null || !IsStorageMetadataOnly(table);
    }

    private static bool IsStorageMetadataOnly(TomlTable table)
    {
        if (table.Keys.Count == 0)
        {
            return false;
        }

        foreach (var key in table.Keys)
        {
            if (!string.Equals(key, StorageModeKey, StringComparison.Ordinal)
                && !string.Equals(key, AppDataDirectoriesKey, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreSameDirectory(string left, string right)
    {
        return TryNormalizeDirectory(left, out var normalizedLeft)
            && TryNormalizeDirectory(right, out var normalizedRight)
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectoryInAppDataOverrides(string appDataTomlPath, string executableDirectory)
    {
        if (!TryNormalizeDirectory(executableDirectory, out var target))
        {
            return false;
        }

        return ReadDirectoryListFromFile(appDataTomlPath)
            .Any(dir => string.Equals(dir, target, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> ReadDirectoryListFromFile(string tomlPath)
    {
        if (!File.Exists(tomlPath))
        {
            return [];
        }

        var table = TryParseToml(tomlPath);

        return table is null ? [] : ReadDirectoryList(table);
    }

    private static List<string> ReadDirectoryList(TomlTable table)
    {
        var result = new List<string>();

        if (!table.TryGetValue(AppDataDirectoriesKey, out var raw) || raw is not TomlArray array)
        {
            return result;
        }

        foreach (var item in array)
        {
            if (item is string text
                && TryNormalizeDirectory(text, out var normalized)
                && !result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    private static bool MergeAppDataDirectories(TomlTable table, List<string> additional)
    {
        var entries = ReadDirectoryList(table);
        var changed = false;

        foreach (var directory in additional)
        {
            if (!entries.Contains(directory, StringComparer.OrdinalIgnoreCase))
            {
                entries.Add(directory);
                changed = true;
            }
        }

        if (changed)
        {
            WriteAppDataDirectories(table, entries);
        }

        return changed;
    }

    private static bool UpdateAppDataDirectories(TomlTable table, string directory, bool include)
    {
        if (!TryNormalizeDirectory(directory, out var target))
        {
            return false;
        }

        var hadArray = table.TryGetValue(AppDataDirectoriesKey, out var raw) && raw is TomlArray;
        var entries = ReadDirectoryList(table);
        var index = entries.FindIndex(entry => string.Equals(entry, target, StringComparison.OrdinalIgnoreCase));

        if (include)
        {
            if (index >= 0 && hadArray)
            {
                return false;
            }

            if (index < 0)
            {
                entries.Add(target);
            }
        }
        else
        {
            if (index < 0 && !hadArray)
            {
                return false;
            }

            if (index >= 0)
            {
                entries.RemoveAt(index);
            }
            else if (hadArray && raw is TomlArray existing && existing.Count == entries.Count)
            {
                return false;
            }
        }

        if (entries.Count == 0)
        {
            return table.Remove(AppDataDirectoriesKey);
        }

        WriteAppDataDirectories(table, entries);
        return true;
    }

    private static void WriteAppDataDirectories(TomlTable table, List<string> entries)
    {
        var array = new TomlArray();

        foreach (var entry in entries)
        {
            array.Add(entry);
        }

        table[AppDataDirectoriesKey] = array;
    }

    private static TomlTable? TryParseToml(string path)
    {
        try
        {
            return TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void UpdateToml(string path, Func<TomlTable, bool> mutate)
    {
        var table = TomlSettingsFile.Read(path);

        if (mutate(table))
        {
            TomlSettingsFile.Save(path, table);
        }
    }

    private static bool IsOwnLegacySettings(string path)
    {
        try
        {
            return File.Exists(path)
                && File.ReadLines(path)
                    .Select(static line => line.Trim())
                    .Any(static line => line.StartsWith(OwnSettingsKeyPrefix, StringComparison.Ordinal) && line.Contains(' '));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryMigrateLegacy(string tomlPath)
    {
        try
        {
            TomlSettingsFile.LoadOrMigrate(tomlPath);
            return File.Exists(tomlPath);
        }
        catch (Exception)
        {
            return false;
        }
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

    private static bool IsSharedHost(string name)
    {
        return string.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("testhost", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("vstest", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("ReSharperTestRunner", StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureExists(string directory)
    {
        Directory.CreateDirectory(directory);
        return directory;
    }
}
