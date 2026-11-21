namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanNodeViewModel : ObservableObject
{
    private static readonly ScanNodeViewModel Dummy = new();
    private static readonly System.ComponentModel.PropertyChangedEventArgs AllPropertiesChanged = new(string.Empty);

    private readonly ScanSortState? _sort;
    private readonly ScanNodeFactory? _factory;
    private RangeObservableCollection<ScanNodeViewModel>? _children;
    private IReadOnlyList<ScanNodeViewModel>? _previewTiles;
    private bool _loaded;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isMarkedDeleted;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isPrimarySelected;

    public RangeObservableCollection<ScanNodeViewModel> Children => _children ??= [];

    public bool IsDirectory => Space is DirectorySpace;

    public bool HasChildren => Space is DirectorySpace dir && (dir.FileCount > 0 || dir.SubDirectoriesArray.Length > 0);

    public string Name => Space?.Name ?? string.Empty;

    public string SizeText => Space is null ? string.Empty : SizeFormatter.Format(IsIgnored ? Space.TotalSize : (long)Weight);

    public double Fraction
    {
        get
        {
            if (Space is null || IsIgnored)
            {
                return 0;
            }

            if (ShowsDriveShare)
            {
                return DriveShare;
            }

            if (field > 0)
            {
                return Math.Clamp(Weight / field, 0, 1);
            }

            return 1;
        }
        private set;
    }

    public double Share
    {
        get => Space is null || IsIgnored || field <= 0 ? 0 : Math.Clamp(Weight / field, 0, 1);
        private set;
    }

    public SpaceBase? Root { get; }

    public bool IsRoot => Space is not null && ReferenceEquals(Space, Root);

    public double ShareOfRoot => Space is null || Root is null || VisibleSize(Root) <= 0
        ? 0
        : Math.Clamp(Weight / VisibleSize(Root), 0, 1);

    public DriveCapacity? Drive { get; }

    public bool ShowsDriveShare => IsRoot && Drive is { TotalBytes: > 0 };

    public double DriveShare => ShowsDriveShare && Space is not null
        ? Math.Clamp((double)Space.TotalSize / Drive!.Value.TotalBytes, 0, 1)
        : 0;

    public string DriveHint => ShowsDriveShare
        ? $"Диск {Drive!.Value.Name} – занято {SizeFormatter.Format(Drive.Value.UsedBytes)} из {SizeFormatter.Format(Drive.Value.TotalBytes)}"
        : string.Empty;

    public double Weight => Space is null || IsIgnored ? 0 : VisibleSize(Space);

    public string ShareText => IsIgnored ? "–" : ShareFormatter.Format(ShowsDriveShare ? DriveShare : Share);

    public string FileCountText => Space is DirectorySpace dir
        ? (IsIgnored ? dir.TotalFileCount : VisibleFileCount(dir)).ToString("N0")
        : string.Empty;

    public string AbsolutePath => Space?.AbsolutePath ?? string.Empty;

    public string Tooltip
    {
        get
        {
            if (IsIgnored)
            {
                return $"{Space?.GetTooltipText()}{Environment.NewLine}{IgnoreHint}";
            }

            if (_factory?.Visibility.Count > 0)
            {
                return $"{Name}{Environment.NewLine}{AbsolutePath}{Environment.NewLine}Размер в отображении: {SizeText}";
            }

            return ShowsDriveShare
                ? $"{Space!.GetTooltipText()}{Environment.NewLine}Доля диска: {ShareFormatter.Format(DriveShare)} – {DriveHint}"
                : Space?.GetTooltipText() ?? string.Empty;
        }
    }

    public string ShareHint => ShowsDriveShare
        ? $"Доля диска: {ShareText} ({DriveHint})"
        : $"Доля от родителя: {ShareText}";

    public string FileCountHint => $"Файлов: {FileCountText} – вместе с вложенными папками";

    public string SizeHint => IsDirectory
        ? $"Размер: {SizeText} – вместе с вложенными папками"
        : $"Размер: {SizeText}";

    public string KindText => IsDirectory ? "Каталог" : "Файл";

    public bool HasOwnSize => OwnSize > 0;

    public string OwnSizeText => Space is DirectorySpace ? SizeFormatter.Format(OwnSize) : string.Empty;

    public string CreationDateText => Space?.CreationDate.ToString("g") ?? string.Empty;

    public string LastAccessText => Space?.LastAccessTime.ToString("g") ?? string.Empty;

    public SpaceBase? Space { get; }

    public bool HasPreviewTiles => IsDirectory && PreviewTiles.Count > 0;

    public bool CanAskAgent => Space is not null && _factory?.ChatEnabled == true;

    public bool CanMarkContentsDeleted => Space is DirectorySpace dir && HasAddedChild(dir);

    public bool CanUnmarkContents => IsDirectory && HasMarkedContents;

    public IReadOnlyList<ScanNodeViewModel> PreviewTiles => _previewTiles ??= BuildPreviewTiles();

    internal bool HasIgnoredItems => _factory?.Visibility.Count > 0;

    private long OwnSize => Space is DirectorySpace dir
        ? IsIgnored ? dir.Size : _factory?.Visibility.OwnSizeOf(dir) ?? dir.Size
        : 0;

    private bool HasMarkedContents => _factory?.MarksPresent == true
        && Space is DirectorySpace dir
        && HasMarkedChild(dir);

    internal ScanNodeViewModel(
        SpaceBase space,
        double siblingMax,
        double parentTotal,
        SpaceBase root,
        DriveCapacity? drive,
        ScanSortState sort,
        ScanNodeFactory factory,
        ScanNodeViewModel? parent = null)
    {
        Space = space;
        Fraction = siblingMax;
        Share = parentTotal;
        Root = root;
        Drive = drive;
        _sort = sort;
        _factory = factory;
        ParentNode = parent;
        _isMarkedDeleted = space.IsDeleted;
        _isIgnored = factory.Visibility.IsIgnored(space);

        if (HasChildren)
        {
            Children.Add(Dummy);
        }
    }

    private ScanNodeViewModel()
    {
    }

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

        var subs = dir.SubDirectoriesArray;
        var items = new List<SpaceBase>(subs.Length + dir.FileCount);

        for (var i = 0; i < subs.Length; i++)
        {
            items.Add(subs[i]);
        }

        for (var i = 0; i < dir.FileCount; i++)
        {
            items.Add(dir.GetFile(i));
        }

        var localMax = items.Where(IsVisible).Select(VisibleSize).DefaultIfEmpty().Max();

        var existing = Children.Where(static child => child.Space is not null).ToDictionary(static child => child.Space!);

        var ordered = items
            .OrderBy(static x => x, _sort)
            .Select(item =>
            {
                var child = existing.GetValueOrDefault(item) ?? _factory!.Create(item, localMax, Weight, Root!, _sort, this);
                child.Fraction = localMax;
                child.Share = Weight;
                return child;
            });

        Children.ReplaceAll(ordered);
        _previewTiles = null;
    }

    public void RefreshMarks()
    {
        RefreshMarkRecursive();
    }

    public void NotifyPropertiesChanged()
    {
        OnPropertyChanged(AllPropertiesChanged);
    }

    public void Resort()
    {
        if (!_loaded || _sort is null)
        {
            return;
        }

        var ordered = Children.OrderBy(static child => child.Space!, _sort).ToList();
        Children.ReplaceAll(ordered);
        _previewTiles = null;

        foreach (var child in Children)
        {
            child.Resort();
        }
    }

    internal long VisibleSize(SpaceBase space)
    {
        return _factory?.Visibility.SizeOf(space) ?? space.TotalSize;
    }

    internal bool IsVisible(SpaceBase space)
    {
        return _factory?.Visibility.IsIgnored(space) != true;
    }

    internal bool HasIgnoredFileIn(DirectorySpace dir)
    {
        return _factory?.Visibility.HasIgnoredFileIn(dir) == true;
    }

    internal int VisibleFileCount(DirectorySpace dir)
    {
        return _factory?.Visibility.FileCountOf(dir) ?? dir.TotalFileCount;
    }

    internal int VisibleDirectoryCount(DirectorySpace dir)
    {
        return IsIgnored
            ? dir.TotalDirectoryCount
            : _factory?.Visibility.DirectoryCountOf(dir) ?? dir.TotalDirectoryCount;
    }

    internal void RefreshVisibility(IReadOnlySet<SpaceBase> affected, bool inheritedChange = false)
    {
        if (Space is null || (!inheritedChange && !affected.Contains(Space)))
        {
            return;
        }

        var changed = IsIgnored != (_factory?.Visibility.IsIgnored(Space) == true);
        IsIgnored = _factory?.Visibility.IsIgnored(Space) == true;

        if (IsRoot)
        {
            Fraction = Weight;
            Share = Weight;
        }

        if (_loaded && _children is { Count: > 0 } children)
        {
            RefreshLoadedChildrenVisibility(children, affected, changed || inheritedChange);
        }

        _previewTiles = null;
        NotifyPropertiesChanged();
    }

    private void RefreshLoadedChildrenVisibility(
        RangeObservableCollection<ScanNodeViewModel> children,
        IReadOnlySet<SpaceBase> affected,
        bool propagateChange)
    {
        var count = children.Count;
        var updateShares = !IsIgnored;
        var localMax = updateShares ? ComputeVisibleChildMax(children) : 0;
        var parentWeight = updateShares ? Weight : 0;

        for (var i = 0; i < count; i++)
        {
            var child = children[i];

            if (updateShares)
            {
                child.Fraction = localMax;
                child.Share = parentWeight;
            }

            if (propagateChange || (child.Space is { } space && affected.Contains(space)))
            {
                child.RefreshVisibility(affected, propagateChange);
            }
            else
            {
                child.NotifyPropertiesChanged();
            }
        }
    }

    private long ComputeVisibleChildMax(RangeObservableCollection<ScanNodeViewModel> children)
    {
        var count = children.Count;
        long max = 0;

        for (var i = 0; i < count; i++)
        {
            if (children[i].Space is { } space && IsVisible(space))
            {
                var size = VisibleSize(space);

                if (size > max)
                {
                    max = size;
                }
            }
        }

        return max;
    }

    private IReadOnlyList<ScanNodeViewModel> BuildPreviewTiles()
    {
        EnsureLoaded();
        return ScanTopSelector.SelectTopWeightedNodes(Children, AppDefaults.TreemapPreviewLimit, out _);
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

        if (Space is DirectorySpace || _factory?.RevealFiles == false)
        {
            _factory?.Shell.Open(Space.AbsolutePath);
        }
        else
        {
            _factory?.Shell.Reveal(Space.AbsolutePath);
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
    private void CopyPath()
    {
        if (Space is not null)
        {
            _factory?.RequestCopyPath(this);
        }
    }
}
