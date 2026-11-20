using System.Collections.ObjectModel;
using System.IO;
using System.Security;
using System.Text.Json;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed class DriveCatalog
{
    private const int RecentLimit = 8;

    private readonly ILogger _logger;
    private readonly ISettingsStore? _settings;
    private string? _selectedPath;

    public ObservableCollection<DriveItem> Items { get; } = [];

    public ObservableCollection<DriveItem> Volumes { get; } = [];

    public ObservableCollection<DriveItem> RecentDirectories { get; } = [];

    public string FallbackPath
    {
        get
        {
            if (Volumes.Count > 0)
            {
                return Volumes[0].Path;
            }

            return Items.Count > 0 ? Items[0].Path : string.Empty;
        }
    }

    public DriveCatalog(ILogger logger, ISettingsStore? settings = null)
    {
        _logger = logger;
        _settings = settings;

        SyncVolumes(null);
        LoadRecentTargets();
    }

    internal void Select(string? path)
    {
        _selectedPath = path;

        foreach (var item in Items)
        {
            item.IsSelected = IsSelectedPath(item.Path);
        }
    }

    internal void AddDrive(string path)
    {
        AddDriveCore(path, persist: true);
    }

    internal bool RemoveDrive(string path)
    {
        var item = Items.FirstOrDefault(drive => ScanTreeEditor.SamePath(drive.Path, path));

        if (item is null)
        {
            return false;
        }

        Items.Remove(item);
        var removedRecent = RecentDirectories.Remove(item);
        Volumes.Remove(item);

        if (removedRecent)
        {
            PersistRecentTargets();
        }

        return true;
    }

    internal bool HasDrive(string path)
    {
        return Items.Any(drive => ScanTreeEditor.SamePath(drive.Path, path));
    }

    internal void ReloadLabels(string? selectedPath)
    {
        SyncVolumes(selectedPath);

        foreach (var drive in Items)
        {
            drive.Invalidate();
        }

        LoadDriveLabels();
    }

    internal void RefreshVolume(string rootPath)
    {
        var existing = Volumes.FirstOrDefault(volume => ScanTreeEditor.SamePath(volume.Path, rootPath));

        if (existing is not null)
        {
            existing.Invalidate();
            LoadItem(existing);
            return;
        }

        AddDrive(rootPath);

        if (Volumes.FirstOrDefault(volume => ScanTreeEditor.SamePath(volume.Path, rootPath)) is { } added)
        {
            LoadItem(added);
        }
    }

    internal void LoadDriveLabels()
    {
        _ = Task.WhenAll(Items.Select(drive => drive.LoadAsync()))
            .ContinueWith(
                task => _logger.DriveSizesFailed(task.Exception!),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
    }

    private void LoadItem(DriveItem item)
    {
        _ = item.LoadAsync()
            .ContinueWith(
                task => _logger.DriveSizesFailed(task.Exception!),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
    }

    private bool IsSelectedPath(string path)
    {
        return _selectedPath is not null && ScanTreeEditor.SamePath(path, _selectedPath);
    }

    private void SyncVolumes(string? selectedPath)
    {
        string[] names;

        try
        {
            names = DriveInfo.GetDrives().Select(drive => drive.Name).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            _logger.DriveSizesFailed(exception);

            return;
        }

        foreach (var name in names.Where(name => !HasDrive(name)))
        {
            AddDrive(name);
        }

        var vanished = Volumes
            .Where(volume => !names.Contains(volume.Path, StringComparer.OrdinalIgnoreCase)
                && !string.Equals(volume.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var volume in vanished)
        {
            Items.Remove(volume);
            Volumes.Remove(volume);
        }
    }

    private void AddDriveCore(string path, bool persist)
    {
        if (string.IsNullOrWhiteSpace(path) || UncPath.IsIncompleteUnc(path))
        {
            return;
        }

        var item = new DriveItem(path)
        {
            IsSelected = IsSelectedPath(path),
        };

        if (!item.IsDirectory)
        {
            if (HasDrive(path))
            {
                return;
            }

            Items.Add(item);
            Volumes.Add(item);

            return;
        }

        var known = RecentDirectories.FirstOrDefault(recent => ScanTreeEditor.SamePath(recent.Path, path));

        if (known is not null)
        {
            var index = RecentDirectories.IndexOf(known);

            if (index > 0)
            {
                RecentDirectories.Move(index, 0);

                if (persist)
                {
                    PersistRecentTargets();
                }
            }

            return;
        }

        Items.Add(item);
        RecentDirectories.Insert(0, item);

        while (RecentDirectories.Count > RecentLimit)
        {
            var evicted = RecentDirectories[^1];

            RecentDirectories.RemoveAt(RecentDirectories.Count - 1);
            Items.Remove(evicted);
        }

        if (UncPath.IsUnc(path))
        {
            NetworkDiscovery.Remember(path);
        }

        if (persist)
        {
            PersistRecentTargets();
        }
    }

    private void LoadRecentTargets()
    {
        if (_settings is null)
        {
            return;
        }

        var json = _settings.GetStringValue(SettingsKeys.ScanRecentTargets);

        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        string[]? saved;

        try
        {
            saved = JsonSerializer.Deserialize<string[]>(json);
        }
        catch (JsonException)
        {
            return;
        }

        if (saved is not { Length: > 0 })
        {
            return;
        }

        var candidates = saved
            .Where(static path => !string.IsNullOrWhiteSpace(path) && !UncPath.IsIncompleteUnc(path))
            .Select(static path => path.Trim())
            .Take(RecentLimit)
            .ToArray();

        for (var i = candidates.Length - 1; i >= 0; i--)
        {
            var target = candidates[i];

            if (!HasDrive(target))
            {
                AddDriveCore(target, persist: false);
            }
        }
    }

    private void PersistRecentTargets()
    {
        if (_settings is null)
        {
            return;
        }

        var paths = RecentDirectories.Select(static item => item.Path).ToArray();
        _settings.SetValue(SettingsKeys.ScanRecentTargets, JsonSerializer.Serialize(paths));
    }
}
