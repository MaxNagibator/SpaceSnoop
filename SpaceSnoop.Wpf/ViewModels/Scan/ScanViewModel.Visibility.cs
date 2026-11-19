namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel
{
    public bool HasIgnoredItems => _nodeFactory.Visibility.Count > 0;

    public string ResetIgnoredCaption => $"Снять игнорирование ({_nodeFactory.Visibility.Count})";
    internal bool IsRefreshingVisibility { get; private set; }

    [RelayCommand]
    public void IgnoreSelected()
    {
        var spaces = ActiveTopLevelNodes()
            .Where(static n => n.CanIgnore)
            .Select(static n => n.Space)
            .OfType<SpaceBase>()
            .ToList();

        if (spaces.Count == 0)
        {
            return;
        }

        var added = _nodeFactory.Visibility.IgnoreMany(spaces);

        if (added.Count > 0)
        {
            RefreshVisibility(AffectedPaths(added));
        }
    }

    [RelayCommand]
    public void UnignoreSelected()
    {
        var spaces = ActiveTopLevelNodes()
            .Where(static n => n.IsIgnored)
            .Select(static n => n.Space)
            .OfType<SpaceBase>()
            .ToList();

        if (spaces.Count == 0)
        {
            return;
        }

        var restored = _nodeFactory.Visibility.RestoreMany(spaces);

        if (restored.Count > 0)
        {
            RefreshVisibility(AffectedPaths(restored));
        }
    }

    private void OnIgnoreRequested(ScanNodeViewModel node)
    {
        if (node.IsSelected && _selectedNodes.Count > 1)
        {
            IgnoreSelected();
            return;
        }

        if (!node.CanIgnore || node.Space is not { } space || !Roots.Any(root => ReferenceEquals(root.Space, node.Root)))
        {
            return;
        }

        if (_nodeFactory.Visibility.Ignore(space))
        {
            RefreshVisibility(AffectedPath(space));
        }
    }

    private void OnUnignoreRequested(ScanNodeViewModel node)
    {
        if (node.IsSelected && _selectedNodes.Count > 1)
        {
            UnignoreSelected();
            return;
        }

        if (node.Space is { } space
            && Roots.Any(root => ReferenceEquals(root.Space, node.Root))
            && _nodeFactory.Visibility.Restore(space) is { } restored)
        {
            RefreshVisibility(AffectedPath(restored));
        }
    }

    private static IReadOnlySet<SpaceBase> AffectedPath(SpaceBase space)
    {
        return AffectedPaths([space]);
    }

    private static IReadOnlySet<SpaceBase> AffectedPaths(IEnumerable<SpaceBase> spaces)
    {
        var affected = new HashSet<SpaceBase>();

        foreach (var space in spaces)
        {
            for (var current = space; current is not null; current = current.Parent)
            {
                affected.Add(current);
            }
        }

        return affected;
    }

    [RelayCommand]
    private void ResetIgnored()
    {
        var affected = _nodeFactory.Visibility.AffectedSpaces();
        _nodeFactory.Visibility.Clear();
        RefreshVisibility(affected);
    }

    private void RefreshVisibility(IReadOnlySet<SpaceBase> affected)
    {
        IsRefreshingVisibility = true;

        try
        {
            foreach (var root in Roots)
            {
                root.RefreshVisibility(affected);
            }
        }
        finally
        {
            IsRefreshingVisibility = false;
        }

        if (_selectedNodes.Count > 1)
        {
            UpdateSelectionSummaryAndInspector(SelectedNode);
        }
        else if (SelectedNode is { } node)
        {
            Inspector.RefreshVisibility(node, affected);
            node.RefreshSelectionState();
        }

        Treemap.RefreshVisibility(_nodeFactory.Visibility, affected);
        OnPropertyChanged(nameof(HasIgnoredItems));
        OnPropertyChanged(nameof(ResetIgnoredCaption));
    }
}
