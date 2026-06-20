using System.Globalization;
using System.IO;
using System.Security;
using System.Windows.Data;

namespace SpaceSnoop.Wpf.Converters;

public sealed class DriveLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return value ?? string.Empty;
        }

        try
        {
            var directory = new DirectoryInfo(path);
            var full = directory.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = directory.Root.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
