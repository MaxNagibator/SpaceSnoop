using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanNodeViewModel : ObservableObject
{
    private static readonly ScanNodeViewModel Dummy = new();

    private readonly ScanSortState? _sort;
    private readonly ILogger _logger;
    private readonly ScanNodeFactory? _factory;
    private bool _loaded;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isMarkedDeleted;

    [ObservableProperty]
    private bool _isSelected;

    internal ScanNodeViewModel(SpaceBase space, double siblingMax, double parentTotal, SpaceBase root, DriveCapacity? drive, ScanSortState sort, ILogger logger, ScanNodeFactory factory)
    {
        Space = space;
        Fraction = siblingMax;
        Share = parentTotal;
        Root = root;
        Drive = drive;
        _sort = sort;
        _logger = logger;
        _factory = factory;
        _isMarkedDeleted = space.IsDeleted;

        if (HasChildren)
        {
            Children.Add(Dummy);
        }
    }

    private ScanNodeViewModel()
    {
        _logger = NullLogger.Instance;
    }

    public RangeObservableCollection<ScanNodeViewModel> Children { get; } = [];

    public bool IsDirectory => Space is DirectorySpace;

    public bool HasChildren => Space is DirectorySpace dir && dir.SubDirectories.Count + dir.Files.Count > 0;

    public string Name => Space?.Name ?? string.Empty;

    public string SizeText => Space is DirectorySpace dir ? dir.TotalSizeText : Space?.SizeText ?? string.Empty;

    public double Fraction
    {
        get
        {
            if (Space is null)
            {
                return 0;
            }

            if (ShowsDriveShare)
            {
                return DriveShare;
            }

            if (field > 0)
            {
                return Math.Clamp(Space.TotalSize / field, 0, 1);
            }

            return 1;
        }
    }

    public double Share => Space is null || field <= 0
        ? 0
        : Math.Clamp(Space.TotalSize / field, 0, 1);

    public SpaceBase? Root { get; }

    public bool IsRoot => Space is not null && ReferenceEquals(Space, Root);

    public double ShareOfRoot => Space is null || Root is null || Root.TotalSize <= 0
        ? 0
        : Math.Clamp((double)Space.TotalSize / Root.TotalSize, 0, 1);

    public DriveCapacity? Drive { get; }

    public bool ShowsDriveShare => IsRoot && Drive is { TotalBytes: > 0 };

    public double DriveShare => ShowsDriveShare && Space is not null
        ? Math.Clamp((double)Space.TotalSize / Drive!.Value.TotalBytes, 0, 1)
        : 0;

    public string DriveHint => ShowsDriveShare
        ? $"Диск {Drive!.Value.Name} – занято {SizeFormatter.Format(Drive.Value.UsedBytes)} из {SizeFormatter.Format(Drive.Value.TotalBytes)}"
        : string.Empty;

    public double Weight => Space?.TotalSize ?? 0;

    public string ShareText => ShareFormatter.Format(ShowsDriveShare ? DriveShare : Share);

    public string FileCountText => Space is DirectorySpace dir ? dir.TotalFileCount.ToString("N0") : string.Empty;

    public string AbsolutePath => Space?.AbsolutePath ?? string.Empty;

    public string Tooltip => ShowsDriveShare
        ? $"{Space!.GetTooltipText()}{Environment.NewLine}Доля диска: {ShareFormatter.Format(DriveShare)} – {DriveHint}"
        : Space?.GetTooltipText() ?? string.Empty;

    public string KindText => IsDirectory ? "Каталог" : "Файл";

    public bool HasOwnSize => Space is DirectorySpace { Size: > 0 };

    public string OwnSizeText => Space is DirectorySpace dir ? dir.SizeText : string.Empty;

    public string CreationDateText => Space?.CreationDate.ToString("g") ?? string.Empty;

    public string LastAccessText => Space?.LastAccessTime.ToString("g") ?? string.Empty;

    public SpaceBase? Space { get; }

    public bool HasPreviewTiles => IsDirectory && PreviewTiles.Count > 0;

    public bool CanAskAgent => Space is not null && _factory?.ChatEnabled == true;

    public bool CanMarkContentsDeleted => IsDirectory && !HasMarkedContents;

    public bool CanUnmarkContents => IsDirectory && HasMarkedContents;

    public IReadOnlyList<ScanNodeViewModel> PreviewTiles
    {
        get
        {
            EnsureLoaded();

            return Children
                .Where(static c => c.Weight > 0)
                .OrderByDescending(static c => c.Weight)
                .Take(AppDefaults.TreemapPreviewLimit)
                .ToList();
        }
    }

    private bool HasMarkedContents => Space is DirectorySpace dir && EnumerateChildren(dir).Any(HasDeletedRecursive);

    public void EnsureLoaded()
    {
        if (_loaded || Space is not DirectorySpace || _sort is null)
        {
            return;
        }

        _loaded = true;
        ReloadChildren();
    }

    public void ReloadChildren()
    {
        if (Space is not DirectorySpace dir || _sort is null)
        {
            return;
        }

        _loaded = true;

        var items = EnumerateChildren(dir).ToList();

        var localMax = items.Count > 0 ? items.Max(static x => x.TotalSize) : 0;

        var ordered = items
            .OrderBy(static x => x, _sort)
            .Select(item => _factory!.Create(item, localMax, dir.TotalSize, Root!, _sort));

        Children.ReplaceAll(ordered);
    }

    public void RefreshMarks()
    {
        RefreshMarkRecursive();
    }

    public void NotifyPropertiesChanged()
    {
        OnPropertyChanged(string.Empty);
    }

    public void Resort()
    {
        if (!_loaded || _sort is null)
        {
            return;
        }

        var ordered = Children.OrderBy(static child => child.Space!, _sort).ToList();
        Children.ReplaceAll(ordered);

        foreach (var child in Children)
        {
            child.Resort();
        }
    }

    private static IEnumerable<SpaceBase> EnumerateChildren(DirectorySpace dir)
    {
        return dir.SubDirectories.Cast<SpaceBase>().Concat(dir.Files);
    }

    internal static void RestoreRecursive(SpaceBase space)
    {
        if (space.IsDeleted)
        {
            space.Restore();
        }

        if (space is not DirectorySpace dir)
        {
            return;
        }

        foreach (var sub in dir.SubDirectories)
        {
            RestoreRecursive(sub);
        }

        foreach (var file in dir.Files)
        {
            if (file.IsDeleted)
            {
                file.Restore();
            }
        }
    }

    private static bool HasDeletedRecursive(SpaceBase space)
    {
        if (space.IsDeleted)
        {
            return true;
        }

        return space is DirectorySpace dir && EnumerateChildren(dir).Any(HasDeletedRecursive);
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            EnsureLoaded();
        }
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        if (Space is null)
        {
            return;
        }

        try
        {
            if (Space is DirectorySpace || _factory?.RevealFiles == false)
            {
                Process.Start(new ProcessStartInfo(Space.AbsolutePath) { UseShellExecute = true });
            }
            else
            {
                Process.Start(SystemExecutable.Explorer, $"/select,\"{Space.AbsolutePath}\"");
            }
        }
        catch (Exception ex)
        {
            _logger.OpenExplorerFailed(ex, Space.AbsolutePath);
        }
    }

    [RelayCommand]
    private void ArchiveToZip()
    {
        if (Space is DirectorySpace)
        {
            _factory?.RequestArchive(this);
        }
    }

    [RelayCommand]
    private void AskAgent()
    {
        if (Space is not null)
        {
            _factory?.RequestAskAgent(this);
        }
    }

    [RelayCommand]
    private void MarkDeleted()
    {
        if (Space is null || Space.IsDeleted)
        {
            return;
        }

        Space.Delete();
        ApplyMarkChange();
    }

    [RelayCommand]
    private void UnmarkDeleted()
    {
        if (Space is null)
        {
            return;
        }

        RestoreRecursive(Space);
        ApplyMarkChange();
    }

    [RelayCommand]
    private void MarkContentsDeleted()
    {
        if (Space is not DirectorySpace dir)
        {
            return;
        }

        foreach (var child in EnumerateChildren(dir))
        {
            child.Delete();
        }

        ApplyMarkChange();
    }

    [RelayCommand]
    private void UnmarkContents()
    {
        if (Space is not DirectorySpace dir)
        {
            return;
        }

        foreach (var child in EnumerateChildren(dir))
        {
            RestoreRecursive(child);
        }

        ApplyMarkChange();
    }

    private void ApplyMarkChange()
    {
        RefreshMarkRecursive();
        _factory?.RaiseMarksChanged();
    }

    private void RefreshMarkRecursive()
    {
        if (Space is not null)
        {
            IsMarkedDeleted = Space.IsDeleted;
            OnPropertyChanged(nameof(CanMarkContentsDeleted));
            OnPropertyChanged(nameof(CanUnmarkContents));
        }

        foreach (var child in Children)
        {
            if (!ReferenceEquals(child, Dummy))
            {
                child.RefreshMarkRecursive();
            }
        }
    }
}
