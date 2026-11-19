namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanNodeViewModel
{
    public bool IsMultiSelected => IsSelected && _factory?.SelectionCount?.Invoke() > 1;

    public bool IsSingleSelection => !IsMultiSelected;

    public string SelectionSummaryText => _factory?.SelectionSummary?.Invoke() ?? string.Empty;

    public bool CanMarkDeletedSelection => IsMultiSelected
        ? _factory?.CanMarkSelectedDeleted?.Invoke() == true
        : !IsMarkedDeleted;

    public bool CanUnmarkDeletedSelection => IsMultiSelected
        ? _factory?.CanUnmarkSelectedDeleted?.Invoke() == true
        : IsMarkedDeleted;

    public bool CanShowMarkContentsDeleted => !IsMultiSelected && CanMarkContentsDeleted;

    public bool CanShowUnmarkContents => !IsMultiSelected && CanUnmarkContents;

    public bool CanShowDirectorySingleActions => !IsMultiSelected && IsDirectory;

    public bool CanShowAskAgent => !IsMultiSelected && CanAskAgent;

    public string CopyPathCaption => IsMultiSelected ? "Копировать пути" : "Копировать путь";

    internal bool HasMarkedSelfOrContents => IsMarkedDeleted || HasMarkedContents;

    internal ScanNodeViewModel? ParentNode { get; }

    internal IEnumerable<ScanNodeViewModel> RealizedChildren => _children?.Where(static child => !ReferenceEquals(child, Dummy)) ?? [];

    internal static bool IsAttachedToRoot(SpaceBase root, SpaceBase candidate)
    {
        for (var current = candidate; !ReferenceEquals(current, root); current = current.Parent)
        {
            if (current.Parent is not DirectorySpace parent)
            {
                return false;
            }

            var attached = current switch
            {
                DirectorySpace subDir => parent.SubDirectories.Contains(subDir, ReferenceEqualityComparer.Instance),
                FileSpace file => parent.Files.Contains(file, ReferenceEqualityComparer.Instance),
                _ => false,
            };

            if (!attached)
            {
                return false;
            }
        }

        return true;
    }

    internal static bool HasMarkedAncestor(SpaceBase space)
    {
        for (var parent = space.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.IsDeleted)
            {
                return true;
            }
        }

        return false;
    }

    internal void RefreshOwnMarks()
    {
        if (Space is null)
        {
            return;
        }

        IsMarkedDeleted = Space.IsDeleted;
        OnPropertyChanged(nameof(CanMarkContentsDeleted));
        OnPropertyChanged(nameof(CanUnmarkContents));
        OnPropertyChanged(nameof(CanMarkDeletedSelection));
        OnPropertyChanged(nameof(CanUnmarkDeletedSelection));
        OnPropertyChanged(nameof(CanShowMarkContentsDeleted));
        OnPropertyChanged(nameof(CanShowUnmarkContents));
    }

    internal void RefreshSelectionState()
    {
        OnPropertyChanged(nameof(IsMultiSelected));
        OnPropertyChanged(nameof(IsSingleSelection));
        OnPropertyChanged(nameof(SelectionSummaryText));
        OnPropertyChanged(nameof(CanMarkDeletedSelection));
        OnPropertyChanged(nameof(CanUnmarkDeletedSelection));
        OnPropertyChanged(nameof(CanShowMarkContentsDeleted));
        OnPropertyChanged(nameof(CanShowUnmarkContents));
        OnPropertyChanged(nameof(CanShowDirectorySingleActions));
        OnPropertyChanged(nameof(CanShowAskAgent));
        OnPropertyChanged(nameof(CopyPathCaption));
        OnPropertyChanged(nameof(CanIgnoreSelection));
        OnPropertyChanged(nameof(CanUnignoreSelection));
        OnPropertyChanged(nameof(EffectiveUnignoreCaption));
    }

    internal void RefreshSubtreeAndAncestorMarks()
    {
        RefreshMarkRecursive();

        for (var parent = ParentNode; parent is not null; parent = parent.ParentNode)
        {
            parent.RefreshOwnMarks();
        }
    }

    partial void OnIsSelectedChanged(bool value)
    {
        RefreshSelectionState();
    }

    partial void OnIsMarkedDeletedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanMarkDeletedSelection));
        OnPropertyChanged(nameof(CanUnmarkDeletedSelection));
        OnPropertyChanged(nameof(CanShowMarkContentsDeleted));
        OnPropertyChanged(nameof(CanShowUnmarkContents));
    }

    [RelayCommand]
    private void MarkDeleted()
    {
        if (IsMultiSelected && _factory?.MarkSelectedDeleted is { } batchMark)
        {
            batchMark();
            return;
        }

        if (Space is null || Space.IsDeleted)
        {
            return;
        }

        var before = _factory?.CaptureMarks(Space, Root ?? Space);
        Space.Delete();
        ApplyMarkChange(before);
    }

    [RelayCommand]
    private void UnmarkDeleted()
    {
        if (IsMultiSelected && _factory?.UnmarkSelectedDeleted is { } batchUnmark)
        {
            batchUnmark();
            return;
        }

        if (Space is null)
        {
            return;
        }

        var before = _factory?.CaptureMarks(Space, Root ?? Space);
        RestoreRecursive(Space);
        ApplyMarkChange(before);
    }

    [RelayCommand]
    private void MarkContentsDeleted()
    {
        if (Space is not DirectorySpace dir)
        {
            return;
        }

        var before = _factory?.CaptureMarks(dir, Root ?? dir);

        foreach (var child in EnumerateChildren(dir))
        {
            child.Delete();
        }

        ApplyMarkChange(before);
    }

    [RelayCommand]
    private void UnmarkContents()
    {
        if (Space is not DirectorySpace dir)
        {
            return;
        }

        var before = _factory?.CaptureMarks(dir, Root ?? dir);

        foreach (var child in EnumerateChildren(dir))
        {
            RestoreRecursive(child);
        }

        ApplyMarkChange(before);
    }

    private void ApplyMarkChange(ScanMarkSnapshot? before)
    {
        if (before is { } snapshot)
        {
            _factory?.RaiseMarksChanged(snapshot);
        }

        RefreshMarkRecursive();

        for (var parent = ParentNode; parent is not null; parent = parent.ParentNode)
        {
            parent.RefreshOwnMarks();
        }
    }

    private void RefreshMarkRecursive()
    {
        RefreshOwnMarks();

        foreach (var child in RealizedChildren)
        {
            child.RefreshMarkRecursive();
        }
    }
}
