using System.IO;
using System.Security;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class DriveItem(string path) : ObservableObject
{
    public string Path { get; } = path;

    [ObservableProperty]
    private string _label = path;

    private Task? _loading;
    private int _generation;

    public Task LoadLabelAsync()
    {
        return _loading ??= LoadCoreAsync(_generation);
    }

    internal void Invalidate()
    {
        _generation++;
        _loading = null;
    }

    private async Task LoadCoreAsync(int generation)
    {
        var label = await Task.Run(() => BuildLabel(Path));

        if (generation == _generation)
        {
            Label = label;
        }
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
