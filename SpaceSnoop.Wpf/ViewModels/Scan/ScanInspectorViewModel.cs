using System.Globalization;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanInspectorViewModel : ObservableObject
{
    public const double DefaultInspectorWidth = 380;
    public const double MinInspectorWidth = 280;
    private const double MaxInspectorWidth = 900;

    private readonly ISettingsStore _settings;
    private readonly IClipboardService _clipboard;
    private bool _suppressPersist;
    private bool _collapsedByUser;
    private bool _autoExpanded;
    private bool _openedAutomatically;
    private bool _compact;
    private bool _collapsedForCompact;
    private bool _reopenedAfterCompact;
    private bool _showedIgnored;
    private IReadOnlyList<ScanNodeViewModel> _selectedTopNodes = [];

    [ObservableProperty]
    private ScanNodeViewModel? _node;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private bool _isMultiSelection;

    [ObservableProperty]
    private bool _isSingleSelection = true;

    [ObservableProperty]
    private bool _showCounts;

    [ObservableProperty]
    private bool _showDates;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private bool _isDirectory;

    [ObservableProperty]
    private string _kindText = string.Empty;

    [ObservableProperty]
    private string _totalSizeText = string.Empty;

    [ObservableProperty]
    private string _ownSizeText = string.Empty;

    [ObservableProperty]
    private bool _hasOwnSize;

    [ObservableProperty]
    private double _shareOfParent;

    [ObservableProperty]
    private string _shareOfParentText = string.Empty;

    [ObservableProperty]
    private double _shareOfRoot;

    [ObservableProperty]
    private string _shareOfRootText = string.Empty;

    [ObservableProperty]
    private double _driveShare;

    [ObservableProperty]
    private string _driveShareText = string.Empty;

    [ObservableProperty]
    private string _driveShareHint = string.Empty;

    [ObservableProperty]
    private bool _showParentShare;

    [ObservableProperty]
    private bool _showRootShare;

    [ObservableProperty]
    private bool _showDriveShare;

    [ObservableProperty]
    private bool _hasShares;

    [ObservableProperty]
    private string _fileCountText = string.Empty;

    [ObservableProperty]
    private string _dirCountText = string.Empty;

    [ObservableProperty]
    private string _creationDateText = string.Empty;

    [ObservableProperty]
    private string _lastAccessText = string.Empty;

    [ObservableProperty]
    private int _moreChildrenCount;

    [ObservableProperty]
    private bool _hasMoreChildren;

    [ObservableProperty]
    private string _moreChildrenText = string.Empty;

    [ObservableProperty]
    private bool _isInspectorCollapsed = true;

    [ObservableProperty]
    private double _intensity = AppDefaults.IntensityDefault;

    public event Action<ScanNodeViewModel>? ChildSelected;

    public RangeObservableCollection<ScanInspectorChild> TopChildren { get; } = [];

    public bool HasTopChildren => TopChildren.Count > 0;

    public double InspectorWidth { get; private set; } = DefaultInspectorWidth;

    public ScanInspectorViewModel(ISettingsStore settings, IClipboardService clipboard)
    {
        _settings = settings;
        _clipboard = clipboard;
        LoadSettings();
    }

    public void SetInspectorWidth(double width)
    {
        var clamped = Math.Clamp(width, MinInspectorWidth, MaxInspectorWidth);

        if (Math.Abs(clamped - InspectorWidth) < 0.5)
        {
            return;
        }

        InspectorWidth = clamped;
        OnPropertyChanged(nameof(InspectorWidth));
        Persist(() => _settings.SetValue(SettingsKeys.ScanInspectorWidth, clamped.ToString("F0", CultureInfo.InvariantCulture)));
    }

    public void SetCompact(bool compact)
    {
        if (compact == _compact)
        {
            if (!compact)
            {
                _reopenedAfterCompact = false;
            }

            return;
        }

        _compact = compact;

        if (compact)
        {
            if (_openedAutomatically)
            {
                _openedAutomatically = false;
                _collapsedForCompact = !_reopenedAfterCompact;
                IsInspectorCollapsed = true;
            }

            return;
        }

        if (_collapsedForCompact)
        {
            _collapsedForCompact = false;
            _reopenedAfterCompact = true;
            ExpandAutomatically();
        }
        else if (!_autoExpanded && HasSelection)
        {
            _autoExpanded = true;
            ExpandAutomatically();
        }
    }

    public void Show(ScanNodeViewModel node)
    {
        if (!_autoExpanded && !_compact)
        {
            _autoExpanded = true;
            ExpandAutomatically();
        }

        _selectedTopNodes = [node];
        Node = node;
        _showedIgnored = node.IsIgnored;
        HasSelection = true;
        IsMultiSelection = false;
        IsSingleSelection = true;

        var space = node.Space;
        Title = node.Name;
        Path = node.AbsolutePath;
        IsDirectory = node.IsDirectory;
        ShowCounts = node.IsDirectory;
        ShowDates = true;
        KindText = node.KindText;

        TotalSizeText = node.SizeText;

        ApplyShares(node);

        CreationDateText = space?.CreationDate.ToString("g") ?? string.Empty;
        LastAccessText = space?.LastAccessTime.ToString("g") ?? string.Empty;

        if (space is DirectorySpace dir)
        {
            HasOwnSize = node.HasOwnSize;
            OwnSizeText = node.OwnSizeText;
            FileCountText = node.FileCountText;
            DirCountText = node.VisibleDirectoryCount(dir).ToString("N0");
            BuildTopChildren(dir, node);
        }
        else
        {
            HasOwnSize = false;
            OwnSizeText = string.Empty;
            FileCountText = string.Empty;
            DirCountText = string.Empty;
            ClearTopChildren();
        }
    }

    public void Show(IReadOnlyList<ScanNodeViewModel> nodes, ScanNodeViewModel? activeNode = null)
    {
        if (nodes.Count == 0)
        {
            Clear();
            return;
        }

        if (nodes.Count == 1)
        {
            Show(nodes[0]);
            return;
        }

        if (!_autoExpanded && !_compact)
        {
            _autoExpanded = true;
            ExpandAutomatically();
        }

        var topNodes = ScanTreeEditor.FilterTopLevelNodes(nodes);
        _selectedTopNodes = topNodes;
        Node = activeNode ?? nodes[^1];
        HasSelection = true;
        IsMultiSelection = true;
        IsSingleSelection = false;

        var dirCount = nodes.Count(static n => n.IsDirectory);
        var fileCount = nodes.Count - dirCount;
        Title = $"{nodes.Count:N0} {Plural.Word(nodes.Count, "объект", "объекта", "объектов")}";
        KindText = ResolveMultiSelectionKind(dirCount, fileCount);
        IsDirectory = dirCount > 0;
        ShowCounts = true;
        ShowDates = false;
        FileCountText = fileCount.ToString("N0");
        DirCountText = dirCount.ToString("N0");
        Path = topNodes.Count <= 4
            ? string.Join(Environment.NewLine, topNodes.Select(static n => n.AbsolutePath))
            : $"{string.Join(Environment.NewLine, topNodes.Take(4).Select(static n => n.AbsolutePath))}{Environment.NewLine}…ещё {topNodes.Count - 4:N0}";

        TotalSizeText = SizeFormatter.Format(CalculateCombinedBytes(topNodes));
        HasOwnSize = false;
        OwnSizeText = string.Empty;
        ShareOfParent = 0;
        ShareOfParentText = string.Empty;
        ShareOfRoot = 0;
        ShareOfRootText = string.Empty;
        DriveShare = 0;
        DriveShareText = string.Empty;
        DriveShareHint = string.Empty;
        ShowParentShare = false;
        ShowRootShare = false;
        ShowDriveShare = false;
        HasShares = false;
        CreationDateText = string.Empty;
        LastAccessText = string.Empty;
        ClearTopChildren();
    }

    public void Clear()
    {
        _selectedTopNodes = [];
        Node = null;
        HasSelection = false;
        IsMultiSelection = false;
        IsSingleSelection = true;
        ShowCounts = false;
        ShowDates = false;
        Title = string.Empty;
        Path = string.Empty;
        IsDirectory = false;
        KindText = string.Empty;
        TotalSizeText = string.Empty;
        OwnSizeText = string.Empty;
        HasOwnSize = false;
        ShareOfParent = 0;
        ShareOfParentText = string.Empty;
        ShareOfRoot = 0;
        ShareOfRootText = string.Empty;
        DriveShare = 0;
        DriveShareText = string.Empty;
        DriveShareHint = string.Empty;
        ShowParentShare = false;
        ShowRootShare = false;
        ShowDriveShare = false;
        HasShares = false;
        FileCountText = string.Empty;
        DirCountText = string.Empty;
        CreationDateText = string.Empty;
        LastAccessText = string.Empty;
        ClearTopChildren();
    }

    internal void RefreshVisibility(ScanNodeViewModel node, IReadOnlySet<SpaceBase> affected)
    {
        if (_showedIgnored != node.IsIgnored || (node.Space is { } space && affected.Contains(space)))
        {
            Show(node);
        }
        else
        {
            ApplyShares(node);
        }
    }

    [RelayCommand]
    private void ToggleInspectorCollapsed()
    {
        IsInspectorCollapsed = !IsInspectorCollapsed;
        _collapsedByUser = IsInspectorCollapsed;
        _openedAutomatically = false;
        _collapsedForCompact = false;
        _reopenedAfterCompact = false;
        Persist(() => _settings.SetBool(SettingsKeys.ScanInspectorCollapsed, _collapsedByUser));
    }

    private void ExpandAutomatically()
    {
        if (!_collapsedByUser && IsInspectorCollapsed)
        {
            IsInspectorCollapsed = false;
            _openedAutomatically = true;
        }
    }

    private void LoadSettings()
    {
        _suppressPersist = true;

        _collapsedByUser = _settings.GetBool(SettingsKeys.ScanInspectorCollapsed);

        var rawWidth = _settings.GetStringValue(SettingsKeys.ScanInspectorWidth);

        if (!string.IsNullOrWhiteSpace(rawWidth)
            && double.TryParse(rawWidth, NumberStyles.Any, CultureInfo.InvariantCulture, out var width))
        {
            InspectorWidth = Math.Clamp(width, MinInspectorWidth, MaxInspectorWidth);
        }

        _suppressPersist = false;
    }

    private void Persist(Action write)
    {
        if (!_suppressPersist)
        {
            write();
        }
    }
}
