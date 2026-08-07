using System.IO;
using System.Security;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class DriveItem(string path) : ObservableObject
{
    public string Path { get; } = path;

    [ObservableProperty]
    private string _label = path;

    private Task? _loading;

    public Task LoadLabelAsync()
    {
        return _loading ??= LoadCoreAsync();
    }

    internal void Invalidate()
    {
        if (_loading is { IsCompleted: true })
        {
            _loading = null;
        }
    }

    private async Task LoadCoreAsync()
    {
        Label = await Task.Run(() => BuildLabel(Path));
    }

    private static string BuildLabel(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        try
        {
            var directory = new DirectoryInfo(path);
            var full = directory.FullName.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            var root = directory.Root.FullName.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

            if (!string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            var drive = new DriveInfo(directory.Root.FullName);

            if (!drive.IsReady)
            {
                return path;
            }

            var used = drive.TotalSize - drive.TotalFreeSpace;
            return $"{path}  –  занято {SizeFormatter.Format(used)} из {SizeFormatter.Format(drive.TotalSize)}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or SecurityException)
        {
            return path;
        }
    }
}
