using MahApps.Metro.IconPacks;

using System.IO;
using System.Security;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed class DriveItem : ObservableObject
{
    private const double WarningRatio = 0.75;
    private const double CriticalRatio = 0.90;
    private const int TitleSegments = 2;

    private static readonly char[] Separators =
    [
        System.IO.Path.DirectorySeparatorChar,
        System.IO.Path.AltDirectorySeparatorChar,
    ];

    private Task? _loading;
    private int _generation;

    public DriveItem(string path)
    {
        Path = path;
        IsDirectory = !IsVolumeRoot(path);
        Title = BuildTitle(path, IsDirectory);
    }

    public string Path { get; }

    public bool IsDirectory { get; }

    public string Title { get; }

    public string? VolumeLabel { get; private set; }

    public DriveType DriveType { get; private set; }

    public long TotalSize { get; private set; }

    public long FreeSpace { get; private set; }

    public bool IsReady { get; private set; }

    public bool IsProbed { get; private set; }

    public bool IsKnownVolume { get; private set; }

    public long UsedBytes => HasUsage ? TotalSize - FreeSpace : 0;

    public double UsedRatio => HasUsage ? Math.Clamp((double)UsedBytes / TotalSize, 0, 1) : 0;

    public bool HasUsage => !IsDirectory && IsReady && TotalSize > 0;

    public bool IsMissingMedia => IsProbed && IsKnownVolume && !IsDirectory && !IsReady;

    public DriveUsageLevel UsageLevel => HasUsage ? LevelFor(UsedRatio) : DriveUsageLevel.None;

    public string? SizeCaption => HasUsage
        ? $"занято {SizeFormatter.Format(UsedBytes)} из {SizeFormatter.Format(TotalSize)}"
        : null;

    public string? Caption => SizeCaption ?? (IsMissingMedia ? "нет носителя" : null);

    public string? RatioCaption => HasUsage ? $"{Math.Round(UsedRatio * 100)} %" : null;

    public string? TypeHint => IsDirectory ? null : HintFor(DriveType);

    public PackIconLucideKind IconKind => IsDirectory ? PackIconLucideKind.Folder : IconFor(DriveType);

    public string AutomationName
    {
        get
        {
            var kind = IsDirectory ? "Каталог" : "Диск";

            return Caption is { } caption ? $"{kind} {Title}, {caption}" : $"{kind} {Title}";
        }
    }

    public Task LoadAsync()
    {
        return _loading ??= LoadCoreAsync(_generation);
    }

    internal void Invalidate()
    {
        _generation++;
        _loading = null;
    }

    private static DriveUsageLevel LevelFor(double ratio)
    {
        if (ratio > CriticalRatio)
        {
            return DriveUsageLevel.Critical;
        }

        return ratio >= WarningRatio ? DriveUsageLevel.Warning : DriveUsageLevel.Normal;
    }

    private static PackIconLucideKind IconFor(DriveType type)
    {
        return type switch
        {
            DriveType.Removable => PackIconLucideKind.Usb,
            DriveType.Network => PackIconLucideKind.Network,
            DriveType.CDRom => PackIconLucideKind.Disc,
            _ => PackIconLucideKind.HardDrive,
        };
    }

    private static string? HintFor(DriveType type)
    {
        return type switch
        {
            DriveType.Removable => "съёмный",
            DriveType.Network => "сетевой",
            DriveType.CDRom => "оптический",
            DriveType.Ram => "в памяти",
            _ => null,
        };
    }

    private static bool IsVolumeRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var directory = new DirectoryInfo(path);
            var full = directory.FullName.TrimEnd(Separators);
            var root = directory.Root.FullName.TrimEnd(Separators);

            return full.Length > 0 && string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or SecurityException)
        {
            return false;
        }
    }

    private static string BuildTitle(string path, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var trimmed = path.TrimEnd(Separators);

        if (trimmed.Length == 0)
        {
            return path;
        }

        if (!isDirectory)
        {
            return trimmed;
        }

        var parts = trimmed.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

        return parts.Length <= TitleSegments
            ? trimmed
            : $"…{System.IO.Path.DirectorySeparatorChar}{string.Join(System.IO.Path.DirectorySeparatorChar, parts[^TitleSegments..])}";
    }

    private static DriveProbe Probe(string path, bool isDirectory)
    {
        if (isDirectory || string.IsNullOrWhiteSpace(path))
        {
            return default;
        }

        try
        {
            var drive = new DriveInfo(path);

            if (!drive.IsReady)
            {
                return new(true, false, null, drive.DriveType, 0, 0);
            }

            var label = drive.VolumeLabel;

            return new(true,
                true,
                string.IsNullOrWhiteSpace(label) ? null : label,
                drive.DriveType,
                drive.TotalSize,
                drive.TotalFreeSpace);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or SecurityException)
        {
            return default;
        }
    }

    private async Task LoadCoreAsync(int generation)
    {
        var probe = await Task.Run(() => Probe(Path, IsDirectory));

        if (generation != _generation)
        {
            return;
        }

        VolumeLabel = probe.VolumeLabel;
        DriveType = probe.Type;
        TotalSize = probe.TotalSize;
        FreeSpace = probe.FreeSpace;
        IsReady = probe.IsReady;
        IsKnownVolume = probe.IsKnownVolume;
        IsProbed = true;

        OnPropertyChanged(string.Empty);
    }

    private readonly record struct DriveProbe(bool IsKnownVolume, bool IsReady, string? VolumeLabel, DriveType Type, long TotalSize, long FreeSpace);
}
