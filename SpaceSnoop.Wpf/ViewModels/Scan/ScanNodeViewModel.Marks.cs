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

    internal void SyncUnloadedChildren()
    {
        if (_loaded)
        {
            return;
        }

        _previewTiles = null;

        if (HasChildren)
        {
            if (_children is null || _children.Count == 0)
            {
                Children.Add(Dummy);
            }
        }
        else if (_children is { Count: > 0 })
        {
            _children.Clear();
        }
    }

    internal static bool IsAttachedToRoot(SpaceBase root, SpaceBase candidate)
    {
        for (var current = candidate; !ReferenceEquals(current, root); current = current.Parent)
        {
            if (current.Parent is not DirectorySpace parent || !parent.ContainsChild(current))
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

        var subs = dir.SubDirectoriesArray;

        for (var i = 0; i < subs.Length; i++)
        {
            RestoreRecursive(subs[i]);
        }

        var files = dir.FileEntries;

        for (var i = 0; i < files.Length; i++)
        {
            if (files[i].IsDeleted)
            {
                dir.GetFile(i).Restore();
            }
        }
    }

    private static bool HasAddedChild(DirectorySpace dir)
    {
        var subs = dir.SubDirectoriesArray;

        for (var i = 0; i < subs.Length; i++)
        {
            if (subs[i].State == SpaceState.Added)
            {
                return true;
            }
        }

        var files = dir.FileEntries;

        for (var i = 0; i < files.Length; i++)
        {
            if (files[i].State == SpaceState.Added)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMarkedChild(DirectorySpace dir)
    {
        var subs = dir.SubDirectoriesArray;

        for (var i = 0; i < subs.Length; i++)
        {
            if (ScanTreeEditor.HasMarkedSelfOrChild(subs[i]))
            {
                return true;
            }
        }

        var files = dir.FileEntries;

        for (var i = 0; i < files.Length; i++)
        {
            if (files[i].IsDeleted)
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
        var subs = dir.SubDirectoriesArray;

        for (var i = 0; i < subs.Length; i++)
        {
            subs[i].Delete();
        }

        for (var i = 0; i < dir.FileCount; i++)
        {
            dir.GetFile(i).Delete();
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
        var subs = dir.SubDirectoriesArray;

        for (var i = 0; i < subs.Length; i++)
        {
            RestoreRecursive(subs[i]);
        }

        var files = dir.FileEntries;

        for (var i = 0; i < files.Length; i++)
        {
            if (files[i].IsDeleted)
            {
                dir.GetFile(i).Restore();
            }
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
