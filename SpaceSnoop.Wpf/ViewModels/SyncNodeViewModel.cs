using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class SyncNodeViewModel : ObservableObject
{
    private static readonly SyncAction[] ActionCycle =
    [
        SyncAction.CopyToRight,
        SyncAction.CopyToLeft,
        SyncAction.Skip,
        SyncAction.DeleteLeft,
        SyncAction.DeleteRight,
    ];

    private readonly DirectoryComparison? _dir;
    private readonly FileComparison? _file;
    private readonly SyncViewModel _owner;

    public SyncNodeViewModel(DirectoryComparison dir, int indent, bool isExpanded, long leftSize, long rightSize, SyncViewModel owner)
    {
        _dir = dir;
        _owner = owner;
        Indent = indent;
        IsExpanded = isExpanded;

        LeftSizeText = LeftAbsent ? string.Empty : SizeFormatter.Format(leftSize);
        RightSizeText = RightAbsent ? string.Empty : SizeFormatter.Format(rightSize);
    }

    public SyncNodeViewModel(FileComparison file, int indent, SyncViewModel owner)
    {
        _file = file;
        _owner = owner;
        Indent = indent;

        LeftSizeText = file.LeftSize is { } left ? SizeFormatter.Format(left) : string.Empty;
        RightSizeText = file.RightSize is { } right ? SizeFormatter.Format(right) : string.Empty;
    }

    public int Indent { get; }

    public bool IsExpanded { get; }

    public bool IsDirectory => _dir is not null;

    public bool IsFile => _file is not null;

    public string Name => _dir?.Name ?? _file?.Name ?? string.Empty;

    public ComparisonStatus Status => _dir?.Status ?? _file?.Status ?? ComparisonStatus.Identical;

    public Thickness IndentMargin => new(Indent * AppDefaults.SyncIndentStep, 0, 0, 0);

    public bool LeftAbsent => Status == ComparisonStatus.RightOnly;

    public bool RightAbsent => Status == ComparisonStatus.LeftOnly;

    public string LeftSizeText { get; }

    public string RightSizeText { get; }

    public PackIconLucideKind ExpandIconKind => IsExpanded ? PackIconLucideKind.ChevronDown : PackIconLucideKind.ChevronRight;

    public PackIconLucideKind ActionIconKind
    {
        get
        {
            if (_file is null)
            {
                return PackIconLucideKind.None;
            }

            return Status == ComparisonStatus.Identical
                ? PackIconLucideKind.Equal
                : _file.Action switch
                {
                    SyncAction.CopyToRight => PackIconLucideKind.ArrowRight,
                    SyncAction.CopyToLeft => PackIconLucideKind.ArrowLeft,
                    SyncAction.Skip => PackIconLucideKind.Ban,
                    SyncAction.DeleteLeft or SyncAction.DeleteRight => PackIconLucideKind.Trash2,
                    _ => PackIconLucideKind.Zap,
                };
        }
    }

    public SyncAction? Action => _file?.Action;

    public bool CanCycle => _file is not null && Status != ComparisonStatus.Identical;

    public bool CanDirDelete => IsDirectory && Status is ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly;

    public string DirDeleteHeader => Status == ComparisonStatus.RightOnly ? "Всё удалить справа" : "Всё удалить слева";

    public string ActionHint
    {
        get
        {
            if (_file is null)
            {
                return "Каталог";
            }

            if (Status == ComparisonStatus.Identical)
            {
                return "Файлы идентичны";
            }

            return _file.Action switch
            {
                SyncAction.CopyToRight => "Копировать слева направо",
                SyncAction.CopyToLeft => "Копировать справа налево",
                SyncAction.Skip => "Пропустить",
                SyncAction.DeleteLeft => "Удалить слева",
                SyncAction.DeleteRight => "Удалить справа",
                _ => "Действие не задано — клик выбирает следующее",
            };
        }
    }

    [RelayCommand]
    private void ToggleExpand()
    {
        if (_dir is not null)
        {
            _owner.ToggleExpand(_dir);
        }
    }

    [RelayCommand]
    private void CycleAction()
    {
        if (_file is null || Status == ComparisonStatus.Identical)
        {
            return;
        }

        var index = Array.IndexOf(ActionCycle, _file.Action);
        SetAction(index < 0 ? ActionCycle[0] : ActionCycle[(index + 1) % ActionCycle.Length]);
    }

    [RelayCommand]
    private void CopyToRight()
    {
        SetAction(SyncAction.CopyToRight);
    }

    [RelayCommand]
    private void CopyToLeft()
    {
        SetAction(SyncAction.CopyToLeft);
    }

    [RelayCommand]
    private void Skip()
    {
        SetAction(SyncAction.Skip);
    }

    [RelayCommand]
    private void Delete()
    {
        if (_file is null)
        {
            return;
        }

        SetAction(Status == ComparisonStatus.LeftOnly ? SyncAction.DeleteLeft : SyncAction.DeleteRight);
    }

    [RelayCommand]
    private void DirCopyToRight()
    {
        ApplyToSubtree(SyncAction.CopyToRight);
    }

    [RelayCommand]
    private void DirCopyToLeft()
    {
        ApplyToSubtree(SyncAction.CopyToLeft);
    }

    [RelayCommand]
    private void DirSkip()
    {
        ApplyToSubtree(SyncAction.Skip);
    }

    [RelayCommand]
    private void DirDelete()
    {
        ApplyToSubtree(Status == ComparisonStatus.RightOnly ? SyncAction.DeleteRight : SyncAction.DeleteLeft);
    }

    private void ApplyToSubtree(SyncAction action)
    {
        if (_dir is not null)
        {
            _owner.ApplyToSubtree(_dir, action);
        }
    }

    private void SetAction(SyncAction action)
    {
        if (_file is null || Status == ComparisonStatus.Identical)
        {
            return;
        }

        _file.Action = action;
        OnPropertyChanged(nameof(ActionIconKind));
        OnPropertyChanged(nameof(Action));
        OnPropertyChanged(nameof(ActionHint));
        _owner.NotifyActionsChanged();
    }
}
