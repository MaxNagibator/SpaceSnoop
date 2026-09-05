using System.Collections.ObjectModel;
using System.IO;
using System.Security;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed class DriveCatalog
{
    // TODO: недавние каталоги живут только текущую сессию, персист заводить при запросе «помнить цели между запусками»
    private const int RecentLimit = 8;

    private readonly ILogger _logger;

    public DriveCatalog(ILogger logger)
    {
        _logger = logger;

        SyncVolumes(null);
    }

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

    internal void AddDrive(string path)
    {
        var item = new DriveItem(path);

        if (!item.IsDirectory)
        {
            Items.Add(item);
            Volumes.Add(item);

            return;
        }

        var known = RecentDirectories.FirstOrDefault(recent => string.Equals(recent.Path, path, StringComparison.OrdinalIgnoreCase));

        if (known is not null)
        {
            var index = RecentDirectories.IndexOf(known);

            if (index > 0)
            {
                RecentDirectories.Move(index, 0);
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
    }

    internal bool RemoveDrive(string path)
    {
        var item = Items.FirstOrDefault(drive => string.Equals(drive.Path, path, StringComparison.OrdinalIgnoreCase));

        if (item is null)
        {
            return false;
        }

        Items.Remove(item);
        RecentDirectories.Remove(item);
        Volumes.Remove(item);

        return true;
    }

    internal bool HasDrive(string path)
    {
        return Items.Any(drive => string.Equals(drive.Path, path, StringComparison.OrdinalIgnoreCase));
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

    internal void LoadDriveLabels()
    {
        _ = Task.WhenAll(Items.Select(drive => drive.LoadAsync()))
            .ContinueWith(task => _logger.DriveSizesFailed(task.Exception!),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
    }
}
