using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncNodeViewModel : ObservableObject
{
    private static readonly SyncAction[] LeftOnlyActions = [SyncAction.CopyToRight, SyncAction.Skip, SyncAction.DeleteLeft];
    private static readonly SyncAction[] RightOnlyActions = [SyncAction.CopyToLeft, SyncAction.Skip, SyncAction.DeleteRight];
    private static readonly SyncAction[] BothSidesActions = [SyncAction.CopyToRight, SyncAction.CopyToLeft, SyncAction.Skip];

    private static readonly SyncAction[] DirActionCycle =
    [
        SyncAction.CopyToRight,
        SyncAction.CopyToLeft,
        SyncAction.Skip,
    ];

    private static readonly SyncAction[] OneSidedLeftCycle = [SyncAction.CopyToRight, SyncAction.Skip];
    private static readonly SyncAction[] OneSidedRightCycle = [SyncAction.CopyToLeft, SyncAction.Skip];

    private readonly DirectoryComparison? _dir;
    private readonly FileComparison? _file;
    private readonly SyncViewModel _owner;
    private readonly bool _flat;
    private readonly string? _groupKey;
    private bool _subtreeActionable;
    private SyncAction? _subtreeAction;

    public SyncNodeViewModel(DirectoryComparison dir, int indent, bool isExpanded, long leftSize, long rightSize, SyncViewModel owner, bool flat = false)
    {
        _dir = dir;
        _owner = owner;
        _flat = flat;
        Indent = indent;
        IsExpanded = isExpanded;

        LeftSizeText = LeftAbsent ? string.Empty : SizeFormatter.Format(leftSize);
        RightSizeText = RightAbsent ? string.Empty : SizeFormatter.Format(rightSize);
        LeftModifiedText = LeftAbsent ? string.Empty : FormatModified(dir.LeftModified);
        RightModifiedText = RightAbsent ? string.Empty : FormatModified(dir.RightModified);

        if (dir is { LeftModified: { } leftTime, RightModified: { } rightTime })
        {
            LeftIsNewer = leftTime > rightTime;
            RightIsNewer = rightTime > leftTime;
        }

        (_subtreeActionable, _subtreeAction) = ComputeSubtreeAction(dir);
    }

    public SyncNodeViewModel(FileComparison file, int indent, SyncViewModel owner, bool flat = false)
    {
        _file = file;
        _owner = owner;
        _flat = flat;
        Indent = indent;

        LeftSizeText = file.LeftSize is { } left ? SizeFormatter.Format(left) : string.Empty;
        RightSizeText = file.RightSize is { } right ? SizeFormatter.Format(right) : string.Empty;
        LeftModifiedText = FormatModified(file.LeftModified);
        RightModifiedText = FormatModified(file.RightModified);

        if (file is { LeftModified: { } leftTime, RightModified: { } rightTime })
        {
            LeftIsNewer = leftTime > rightTime;
            RightIsNewer = rightTime > leftTime;
        }
    }

    private SyncNodeViewModel(SyncViewModel owner, string? groupKey, string headerText, int indent, bool expanded)
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
                    : IconFor(_file.Action);
            }

            if (IsOneSidedDir)
            {
                return IconFor(_dir!.Action);
            }

            if (!_subtreeActionable)
            {
                return PackIconLucideKind.None;
            }

            return _subtreeAction is { } action ? IconFor(action) : PackIconLucideKind.Minus;
        }
    }

    public SyncAction? Action => _file?.Action ?? (IsOneSidedDir ? _dir!.Action : _subtreeAction);

    public string DiffReason => _file is null ? string.Empty : DescribeDiff(_file);

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

    public bool CanDeleteFile => IsFile && Status is ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly;

    public string FileDeleteHeader => Status == ComparisonStatus.LeftOnly ? "Удалить слева (в корзину)" : "Удалить справа (в корзину)";

    public bool CanDirDelete => IsDirectory && Status is ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly;

    public string DirDeleteHeader => Status == ComparisonStatus.RightOnly ? "Удалить всё справа (в корзину)" : "Удалить всё слева (в корзину)";

    public string ActionHint
    {
        get
        {
            if (_file is null)
            {
                if (IsOneSidedDir)
                {
                    return _dir!.Action switch
                    {
                        SyncAction.CopyToRight => "Каталог только слева – создать справа (клик меняет)",
                        SyncAction.CopyToLeft => "Каталог только справа – создать слева (клик меняет)",
                        SyncAction.DeleteLeft or SyncAction.DeleteRight => "Каталог будет удалён в корзину (клик: копировать)",
                        _ => "Каталог пропускается (клик: копировать)",
                    };
                }

                if (!_subtreeActionable)
                {
                    return "Каталог";
                }

                return _subtreeAction switch
                {
                    SyncAction.CopyToRight => "Всё слева направо – клик меняет",
                    SyncAction.CopyToLeft => "Всё справа налево – клик меняет",
                    SyncAction.Skip => "Всё пропустить – клик меняет",
                    SyncAction.DeleteLeft or SyncAction.DeleteRight => "Всё удалить – клик задаёт «всё копировать →»",
                    _ => "Разные действия – клик задаёт «всё копировать →»",
                };
            }

            if (Status == ComparisonStatus.Identical)
            {
                return "Файлы идентичны";
            }

            var actionText = _file.Action switch
            {
                SyncAction.CopyToRight => "Копировать слева направо",
                SyncAction.CopyToLeft => "Копировать справа налево",
                SyncAction.Skip => "Пропустить",
                SyncAction.DeleteLeft => "Удалить слева",
                SyncAction.DeleteRight => "Удалить справа",
                _ => "Действие не задано – клик выбирает следующее",
            };

            var reason = DiffReason;
            return reason.Length == 0 ? actionText : $"{actionText}\nРазличие: {reason}";
        }
    }

    private bool IsOneSidedDir => _dir is { Status: ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly };

    private SyncAction[] FileActionCycle => Status switch
    {
        ComparisonStatus.LeftOnly => LeftOnlyActions,
        ComparisonStatus.RightOnly => RightOnlyActions,
        _ => BothSidesActions,
    };

    public static SyncNodeViewModel CreateGroupHeader(int groupedCount, bool expanded, SyncViewModel owner)
    {
        return new(owner, null, $"Служебные файлы ({groupedCount})", 0, expanded);
    }

    public static SyncNodeViewModel CreateSubGroupHeader(string folder, int count, bool expanded, SyncViewModel owner)
    {
        return new(owner, folder, $"{folder} ({count})", 1, expanded);
    }

    public static string DescribeDiff(FileComparison file)
    {
        if (file.Status != ComparisonStatus.Modified)
        {
            return string.Empty;
        }

        var sizeDiffers = file.LeftSize != file.RightSize;
        var delta = file is { LeftModified: { } left, RightModified: { } right } ? (left - right).Duration() : TimeSpan.Zero;
        var timeDiffers = delta > DirectoryComparer.FatTimestampTolerance;

        return (sizeDiffers, timeDiffers) switch
        {
            (true, true) => $"размер и время (Δ {FormatDelta(delta)})",
            (true, false) => "размер",
            (false, true) => $"время (Δ {FormatDelta(delta)})",
            _ => string.Empty,
        };
    }

    public void RefreshSubtreeAction()
    {
        if (_dir is null)
        {
            return;
        }

        (_subtreeActionable, _subtreeAction) = ComputeSubtreeAction(_dir);
        OnPropertyChanged(nameof(ActionIconKind));
        OnPropertyChanged(nameof(Action));
        OnPropertyChanged(nameof(ActionHint));
    }

    internal static string FormatDelta(TimeSpan delta)
    {
        var d = delta.Duration();

        if (d.TotalSeconds < 60)
        {
            return $"{(int)Math.Round(d.TotalSeconds)} с";
        }

        if (d.TotalMinutes < 60)
        {
            return $"{(int)Math.Round(d.TotalMinutes)} мин";
        }

        return $"{d.TotalHours:0.#} ч";
    }

    private static PackIconLucideKind IconFor(SyncAction action)
    {
        return action switch
        {
            SyncAction.CopyToRight => PackIconLucideKind.ArrowRight,
            SyncAction.CopyToLeft => PackIconLucideKind.ArrowLeft,
            SyncAction.Skip => PackIconLucideKind.Ban,
            SyncAction.DeleteLeft or SyncAction.DeleteRight => PackIconLucideKind.Trash2,
            _ => PackIconLucideKind.Zap,
        };
    }

    private static (bool Actionable, SyncAction? Uniform) ComputeSubtreeAction(DirectoryComparison root)
    {
        var actionable = false;
        var first = true;
        var mixed = false;
        var uniform = SyncAction.None;

        Walk(root);

        return (actionable, mixed || !actionable ? null : uniform);

        void Walk(DirectoryComparison dir)
        {
            foreach (var file in dir.Files)
            {
                if (file.Status == ComparisonStatus.Identical)
                {
                    continue;
                }

                actionable = true;

                if (first)
                {
                    uniform = file.Action;
                    first = false;
                }
                else if (file.Action != uniform)
                {
                    mixed = true;
                }
            }

            foreach (var sub in dir.SubDirectories)
            {
                Walk(sub);
            }
        }
    }

    private static string FormatModified(DateTime? value)
    {
        return value is { } dt ? dt.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty;
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
            if (Status == ComparisonStatus.Identical)
            {
                return;
            }

            var cycle = FileActionCycle;
            var index = Array.IndexOf(cycle, _file.Action);
            SetAction(index < 0 ? cycle[0] : cycle[(index + 1) % cycle.Length]);
            return;
        }

        if (_dir is null)
        {
            return;
        }

        if (IsOneSidedDir)
        {
            var cycle = _dir.Status == ComparisonStatus.LeftOnly ? OneSidedLeftCycle : OneSidedRightCycle;
            var index = Array.IndexOf(cycle, _dir.Action);
            ApplyToSubtree(index < 0 ? cycle[0] : cycle[(index + 1) % cycle.Length]);
            return;
        }

        if (_subtreeActionable)
        {
            var index = _subtreeAction is { } current ? Array.IndexOf(DirActionCycle, current) : -1;
            ApplyToSubtree(index < 0 ? DirActionCycle[0] : DirActionCycle[(index + 1) % DirActionCycle.Length]);
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
