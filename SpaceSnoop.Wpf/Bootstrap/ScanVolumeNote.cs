namespace SpaceSnoop.Wpf.Bootstrap;

public static class ScanVolumeNote
{
    public const string Hint = "Скан складывает логический размер файлов, а диск считает занятые кластеры. "
        + "Расходятся они на жёстких ссылках (WinSxS), сжатии Compact OS и разреженных файлах: "
        + "одни и те же байты считаются под несколькими именами либо занимают на диске меньше.";

    public static string Explain(string note)
    {
        return $"Объём скана больше занятого на диске на {note}. {Hint}";
    }

    public static string? Describe(string scannedPath, long scannedBytes, DriveCapacity? drive)
    {
        if (drive is not { UsedBytes: > 0 } capacity || !IsDriveRoot(scannedPath, capacity.Name))
        {
            return null;
        }

        var difference = scannedBytes - capacity.UsedBytes;

        if (difference <= capacity.UsedBytes * AppDefaults.ScanVolumeNoteFraction)
        {
            return null;
        }

        return $"≈{SizeFormatter.Format(difference)}";
    }

    private static bool IsDriveRoot(string scannedPath, string driveName)
    {
        if (string.IsNullOrWhiteSpace(scannedPath))
        {
            return false;
        }

        var trimmed = scannedPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

        return string.Equals(trimmed, driveName, StringComparison.OrdinalIgnoreCase);
    }
}
