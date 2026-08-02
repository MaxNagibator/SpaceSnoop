using System.Collections.ObjectModel;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed class DriveCatalog
{
    private readonly ILogger _logger;

    public DriveCatalog(ILogger logger)
    {
        _logger = logger;

        foreach (var drive in DriveInfo.GetDrives())
        {
            AddDrive(drive.Name);
        }
    }

    public ObservableCollection<DriveItem> Items { get; } = [];

    internal void AddDrive(string path)
    {
        Items.Add(new(path));
    }

    internal bool HasDrive(string path)
    {
        return Items.Any(drive => string.Equals(drive.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    internal void LoadDriveLabels()
    {
        _ = Task.WhenAll(Items.Select(drive => drive.LoadLabelAsync()))
            .ContinueWith(task => _logger.DriveSizesFailed(task.Exception!),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
    }
}
