using System.Globalization;
using System.Text;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanInspectorViewModel : ObservableObject
{
    public const double DefaultInspectorWidth = 380;
    public const double MinInspectorWidth = 280;
    private const double MaxInspectorWidth = 900;

    private readonly ISettingsStore _settings;
    private readonly IClipboardService _clipboard;
    private bool _suppressPersist;

    [ObservableProperty]
    private ScanNodeViewModel? _node;

    [ObservableProperty]
    private bool _hasSelection;

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
    private bool _isInspectorCollapsed;

    [ObservableProperty]
    private double _intensity = AppDefaults.IntensityDefault;

    public ScanInspectorViewModel(ISettingsStore settings, IClipboardService clipboard)
    {
        _settings = settings;
        _clipboard = clipboard;
        LoadSettings();
    }

    public RangeObservableCollection<ScanInspectorChild> TopChildren { get; } = [];

    public bool HasTopChildren => TopChildren.Count > 0;

    public double InspectorWidth { get; private set; } = DefaultInspectorWidth;

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

    public void Show(ScanNodeViewModel node)
    {
        Node = node;
        HasSelection = true;

        var space = node.Space;
        Title = node.Name;
        Path = node.AbsolutePath;
        IsDirectory = node.IsDirectory;
        KindText = node.KindText;

        var total = space?.TotalSize ?? 0;
        TotalSizeText = SizeFormatter.Format(total);

        ApplyShares(node);

        CreationDateText = space?.CreationDate.ToString("g") ?? string.Empty;
        LastAccessText = space?.LastAccessTime.ToString("g") ?? string.Empty;

        if (space is DirectorySpace dir)
        {
            HasOwnSize = dir.Size > 0;
            OwnSizeText = SizeFormatter.Format(dir.Size);
            FileCountText = dir.TotalFileCount.ToString("N0");
            DirCountText = dir.TotalDirectoryCount.ToString("N0");
            BuildTopChildren(dir);
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

    public void Clear()
    {
        Node = null;
        HasSelection = false;
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

    [RelayCommand]
    private void ToggleInspectorCollapsed()
    {
        IsInspectorCollapsed = !IsInspectorCollapsed;
    }

    partial void OnIsInspectorCollapsedChanged(bool value)
    {
        Persist(() => _settings.SetBool(SettingsKeys.ScanInspectorCollapsed, value));
    }

    private void LoadSettings()
    {
        _suppressPersist = true;

        IsInspectorCollapsed = _settings.GetBool(SettingsKeys.ScanInspectorCollapsed);

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

    private void TryCopy(string? value)
    {
        _clipboard.TrySetText(value ?? string.Empty);
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (!HasSelection)
        {
            return;
        }

        TryCopy(Path);
    }

    [RelayCommand]
    private void CopyDetails()
    {
        if (!HasSelection)
        {
            return;
        }

        TryCopy(BuildDetails());
    }

    private void ApplyShares(ScanNodeViewModel node)
    {
        ShareOfParent = node.Share;
        ShareOfParentText = ShareFormatter.Format(node.Share);
        ShareOfRoot = node.ShareOfRoot;
        ShareOfRootText = ShareFormatter.Format(node.ShareOfRoot);

        ShowParentShare = !node.IsRoot;
        ShowRootShare = ShowParentShare && !string.Equals(ShareOfParentText, ShareOfRootText, StringComparison.Ordinal);

        ApplyDriveShare(node);

        HasShares = ShowParentShare || ShowRootShare || ShowDriveShare;
    }

    private void ApplyDriveShare(ScanNodeViewModel node)
    {
        ShowDriveShare = node.ShowsDriveShare;
        DriveShare = node.DriveShare;
        DriveShareText = ShowDriveShare ? ShareFormatter.Format(node.DriveShare) : string.Empty;
        DriveShareHint = node.DriveHint;
    }

    private void BuildTopChildren(DirectorySpace dir)
    {
        var children = dir.SubDirectories
            .Cast<SpaceBase>()
            .Concat(dir.Files)
            .OrderByDescending(static x => x.TotalSize)
            .ToList();

        var max = children.Count > 0 ? children[0].TotalSize : 0;

        var top = children
            .Take(AppDefaults.TopChildrenLimit)
            .Select(child => new ScanInspectorChild(child.Name,
                SizeFormatter.Format(child.TotalSize),
                ShareFormatter.Format(dir.TotalSize > 0 ? (double)child.TotalSize / dir.TotalSize : 0),
                max > 0 ? Math.Clamp((double)child.TotalSize / max, 0, 1) : 0,
                child is DirectorySpace));

        TopChildren.ReplaceAll(top);

        MoreChildrenCount = Math.Max(0, children.Count - AppDefaults.TopChildrenLimit);
        HasMoreChildren = MoreChildrenCount > 0;
        MoreChildrenText = HasMoreChildren ? $"…ещё {MoreChildrenCount}" : string.Empty;

        OnPropertyChanged(nameof(HasTopChildren));
    }

    private void ClearTopChildren()
    {
        TopChildren.ReplaceAll([]);
        MoreChildrenCount = 0;
        HasMoreChildren = false;
        MoreChildrenText = string.Empty;
        OnPropertyChanged(nameof(HasTopChildren));
    }

    private string BuildDetails()
    {
        var builder = new StringBuilder();

        builder.AppendLine($"Название: {Title}");
        builder.AppendLine($"Путь: {Path}");
        builder.AppendLine($"Тип: {KindText}");
        builder.AppendLine($"Общий размер: {TotalSizeText}");

        if (HasOwnSize)
        {
            builder.AppendLine($"Свои файлы: {OwnSizeText}");
        }

        if (ShowParentShare)
        {
            builder.AppendLine($"Доля родителя: {ShareOfParentText}");
        }

        if (ShowRootShare)
        {
            builder.AppendLine($"Доля корня: {ShareOfRootText}");
        }

        if (ShowDriveShare)
        {
            builder.AppendLine($"Доля диска: {DriveShareText} ({DriveShareHint})");
        }

        if (IsDirectory)
        {
            builder.AppendLine($"Файлов: {FileCountText}");
            builder.AppendLine($"Подкаталогов: {DirCountText}");
        }

        builder.AppendLine($"Создан: {CreationDateText}");
        builder.AppendLine($"Последний доступ: {LastAccessText}");

        return builder.ToString();
    }
}

public sealed record ScanInspectorChild(string Name, string SizeText, string ShareText, double Fraction, bool IsDirectory);
