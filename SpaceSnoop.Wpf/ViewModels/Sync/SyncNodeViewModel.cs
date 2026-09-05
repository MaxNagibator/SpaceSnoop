using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncNodeViewModel : ObservableObject
{
    private readonly DirectoryComparison? _dir;
    private readonly FileComparison? _file;
    private readonly ISyncRowHost _owner;
    private readonly bool _flat;
    private readonly string? _groupKey;
    private bool _subtreeActionable;
    private SyncAction? _subtreeAction;

    internal SyncNodeViewModel(DirectoryComparison dir, int indent, bool isExpanded, long leftSize, long rightSize, ISyncRowHost owner, bool flat = false)
    {
        _dir = dir;
        _owner = owner;
        _flat = flat;
        Indent = indent;
        IsExpanded = isExpanded;

        LeftSizeText = LeftAbsent ? string.Empty : SizeFormatter.Format(leftSize);
        RightSizeText = RightAbsent ? string.Empty : SizeFormatter.Format(rightSize);
        LeftModifiedText = LeftAbsent ? string.Empty : SyncNodeText.FormatModified(dir.LeftModified);
        RightModifiedText = RightAbsent ? string.Empty : SyncNodeText.FormatModified(dir.RightModified);

        if (dir is { LeftModified: { } leftTime, RightModified: { } rightTime })
        {
            LeftIsNewer = leftTime > rightTime;
            RightIsNewer = rightTime > leftTime;
        }

        (_subtreeActionable, _subtreeAction) = SyncActionCycles.ComputeSubtree(dir);
    }

    internal SyncNodeViewModel(FileComparison file, int indent, ISyncRowHost owner, bool flat = false)
    {
        _file = file;
        _owner = owner;
        _flat = flat;
        Indent = indent;

        LeftSizeText = file.LeftSize is { } left ? SizeFormatter.Format(left) : string.Empty;
        RightSizeText = file.RightSize is { } right ? SizeFormatter.Format(right) : string.Empty;
        LeftModifiedText = SyncNodeText.FormatModified(file.LeftModified);
        RightModifiedText = SyncNodeText.FormatModified(file.RightModified);

        if (file is { LeftModified: { } leftTime, RightModified: { } rightTime })
        {
            LeftIsNewer = leftTime > rightTime;
            RightIsNewer = rightTime > leftTime;
        }
    }

    private SyncNodeViewModel(ISyncRowHost owner, string? groupKey, string headerText, int indent, bool expanded)
    {
        _owner = owner;
        _flat = true;
        _groupKey = groupKey;
        IsGroupHeader = true;
        IsExpanded = expanded;
        Indent = indent;
        GroupHeaderText = headerText;
    }

    public int Indent { get; }

    public bool IsExpanded { get; }

    public bool IsGroupHeader { get; }

    public string GroupHeaderText { get; } = string.Empty;

    public SyncOutcome Outcome { get; init; }

    public bool IsDirectory => _dir is not null;

    public bool IsFile => _file is not null;

    public bool TreeExpansionActionsVisible => IsDirectory && !_flat;

    public string Name => _dir?.Name ?? _file?.Name ?? string.Empty;

    public string DisplayName => _flat ? _file?.RelativePath ?? _dir?.RelativePath ?? Name : Name;

    public ComparisonStatus Status => _dir?.Status ?? _file?.Status ?? ComparisonStatus.Identical;

    public Thickness IndentMargin => new(Indent * AppDefaults.SyncIndentStep, 0, 0, 0);

    public bool LeftAbsent => Status == ComparisonStatus.RightOnly;

    public bool RightAbsent => Status == ComparisonStatus.LeftOnly;

    public string LeftSizeText { get; } = string.Empty;

    public string RightSizeText { get; } = string.Empty;

    public string LeftModifiedText { get; } = string.Empty;

    public string RightModifiedText { get; } = string.Empty;

    public bool LeftIsNewer { get; }

    public bool RightIsNewer { get; }

    public PackIconLucideKind ExpandIconKind => IsExpanded ? PackIconLucideKind.ChevronDown : PackIconLucideKind.ChevronRight;

    public PackIconLucideKind ActionIconKind
    {
        get
        {
            if (_file is not null)
            {
                return Status == ComparisonStatus.Identical
                    ? PackIconLucideKind.Equal
                    : SyncNodeText.IconFor(_file.Action);
            }

            if (IsOneSidedDir)
            {
                return SyncNodeText.IconFor(_dir!.Action);
            }

            if (!_subtreeActionable)
            {
                return PackIconLucideKind.None;
            }

            return _subtreeAction is { } action ? SyncNodeText.IconFor(action) : PackIconLucideKind.Minus;
        }
    }

    public SyncAction? Action => _file?.Action ?? (IsOneSidedDir ? _dir!.Action : _subtreeAction);

    public string DiffReason => _file is null ? string.Empty : SyncNodeText.DescribeDiff(_file);

    public bool CanCycle => _file is not null ? Status != ComparisonStatus.Identical : _subtreeActionable || IsOneSidedDir;

    public bool ShowAction => IsFile || _subtreeActionable || IsOneSidedDir;

    public bool LeftContentVisible => !(LeftAbsent && _owner.BlankAbsent);

    public bool RightContentVisible => !(RightAbsent && _owner.BlankAbsent);

    public bool LeftExpanderVisible => IsDirectory && LeftContentVisible && !_flat;

    public bool RightExpanderVisible => IsDirectory && RightContentVisible && !_flat;

    public GridLength ExpanderColumnWidth => _flat ? new(0) : new GridLength(14);

    public bool CanCompareContent => _file is not null;

    public bool CanAskAgent => !IsGroupHeader && _owner.ChatEnabled;

    public string RelativePath => _file?.RelativePath ?? _dir?.RelativePath ?? Name;

    public string CompareContentHeader => Status is ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly
        ? "Показать содержимое"
        : "Сравнить содержимое";

    public bool CanCopyRight => IsFile && Status is ComparisonStatus.LeftOnly or ComparisonStatus.Modified or ComparisonStatus.Conflict;

    public bool CanCopyLeft => IsFile && Status is ComparisonStatus.RightOnly or ComparisonStatus.Modified or ComparisonStatus.Conflict;

    public bool CanDirCopyRight => IsDirectory && Status != ComparisonStatus.RightOnly;

    public bool CanDirCopyLeft => IsDirectory && Status != ComparisonStatus.LeftOnly;

    public bool CanDeleteFile => IsFile && Status is ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly;

    public string FileDeleteHeader => Status == ComparisonStatus.LeftOnly ? "Удалить слева (в корзину)" : "Удалить справа (в корзину)";

    public bool CanDirDelete => IsDirectory && Status is ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly;

    public string DirDeleteHeader => Status == ComparisonStatus.RightOnly ? "Удалить всё справа (в корзину)" : "Удалить всё слева (в корзину)";

    public bool HasTypeConflict => _file is { TypeConflict: not FileTypeConflict.None };

    public bool CopyAllowed => !HasTypeConflict;

    public string CopyHint => CopyAllowed
        ? string.Empty
        : "Копирование недоступно: слева и справа объекты разного вида, это разрешается вручную в файловой системе";

    public bool DeleteAllowed => !HasTypeConflict && !DeleteBlocked(Status == ComparisonStatus.RightOnly ? SyncAction.DeleteRight : SyncAction.DeleteLeft);

    public string DeleteHint => DeleteAllowed
        ? string.Empty
        : HasTypeConflict
            ? CopyHint
            : "Удаление недоступно: противоположную сторону обошли не полностью, и «нет файла» здесь неотличимо от «не увидели»";

    public string ActionHint
    {
        get
        {
            if (_file is null)
            {
                if (IsOneSidedDir)
                {
                    return SyncNodeText.OneSidedDirectoryHint(_dir!.Action);
                }

                return _subtreeActionable ? SyncNodeText.SubtreeHint(_subtreeAction) : "Каталог";
            }

            if (Status == ComparisonStatus.Identical)
            {
                return "Файлы идентичны";
            }

            var actionText = SyncNodeText.FileActionHint(_file.Action);
            var reason = DiffReason;

            return reason.Length == 0 ? actionText : $"{actionText}\nРазличие: {reason}";
        }
    }

    private bool IsOneSidedDir => _dir is { Status: ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly };

    internal static SyncNodeViewModel CreateGroupHeader(int groupedCount, bool expanded, ISyncRowHost owner)
    {
        return new(owner, null, $"Служебные файлы ({groupedCount})", 0, expanded);
    }

    internal static SyncNodeViewModel CreateSubGroupHeader(string folder, int count, bool expanded, ISyncRowHost owner)
    {
        return new(owner, folder, $"{folder} ({count})", 1, expanded);
    }

    public void RefreshSubtreeAction()
    {
        if (_dir is null)
        {
            return;
        }

        (_subtreeActionable, _subtreeAction) = SyncActionCycles.ComputeSubtree(_dir);
        OnPropertyChanged(nameof(ActionIconKind));
        OnPropertyChanged(nameof(Action));
        OnPropertyChanged(nameof(ActionHint));
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
    private void ToggleGroup()
    {
        _owner.ToggleGroup(_groupKey);
    }

    [RelayCommand]
    private void ExpandSubtree()
    {
        if (_dir is not null)
        {
            _owner.ExpandSubtree(_dir);
        }
    }

    [RelayCommand]
    private void CollapseSubtree()
    {
        if (_dir is not null)
        {
            _owner.CollapseSubtree(_dir);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCompareContent))]
    private Task CompareContentAsync()
    {
        if (_file is not null)
        {
            return _owner.CompareContentAsync(_file);
        }

        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanAskAgent))]
    private void AskAgent()
    {
        _owner.AskAgentAbout(this);
    }

    [RelayCommand]
    private void CycleAction()
    {
        if (_file is not null)
        {
            CycleFileAction();
            return;
        }

        if (_dir is null)
        {
            return;
        }

        CycleDirectoryAction();
    }

    private void CycleFileAction()
    {
        if (Status == ComparisonStatus.Identical)
        {
            return;
        }

        SetAction(SyncActionCycles.Next(SyncActionCycles.ForFile(Status, DeleteAllowed, HasTypeConflict), _file!.Action));
    }

    private void CycleDirectoryAction()
    {
        if (IsOneSidedDir)
        {
            ApplyToSubtree(SyncActionCycles.Next(SyncActionCycles.ForOneSidedDirectory(_dir!.Status), _dir.Action));
            return;
        }

        if (_subtreeActionable)
        {
            ApplyToSubtree(SyncActionCycles.Next(SyncActionCycles.Directory, _subtreeAction));
        }
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
        if (DeleteBlocked(action))
        {
            return;
        }

        if (_dir is not null)
        {
            _owner.ApplyToSubtree(_dir, action);
        }
    }

    private bool DeleteBlocked(SyncAction action)
    {
        return action switch
        {
            SyncAction.DeleteLeft => _file?.DeleteLeftBlocked ?? _dir?.DeleteLeftBlocked ?? false,
            SyncAction.DeleteRight => _file?.DeleteRightBlocked ?? _dir?.DeleteRightBlocked ?? false,
            _ => false,
        };
    }

    private void SetAction(SyncAction action)
    {
        if (_file is null || Status == ComparisonStatus.Identical || DeleteBlocked(action))
        {
            return;
        }

        if (HasTypeConflict && action != SyncAction.Skip)
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
