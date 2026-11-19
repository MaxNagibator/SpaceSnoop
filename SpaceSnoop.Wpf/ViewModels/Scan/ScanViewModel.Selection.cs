namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel
{
    private readonly List<ScanNodeViewModel> _selectedNodes = [];
    private readonly HashSet<ScanNodeViewModel> _selectedSet = [];
    private ScanNodeViewModel? _selectionAnchor;

    public IReadOnlyList<ScanNodeViewModel> SelectedNodes => _selectedNodes;

    public bool HasMultiSelection => _selectedNodes.Count > 1;

    public string SelectionSummaryText { get; private set; } = string.Empty;

    public void SelectSingle(ScanNodeViewModel? node)
    {
        ApplySelection(node is null ? [] : [node], node, true);
    }

    public void ToggleSelection(ScanNodeViewModel node)
    {
        var list = new List<ScanNodeViewModel>(_selectedNodes);

        if (_selectedSet.Contains(node))
        {
            list.Remove(node);
            var fallback = list.Count > 0 ? list[^1] : null;
            var nextPrimary = ReferenceEquals(SelectedNode, node) ? fallback : SelectedNode ?? fallback;
            _selectionAnchor = ReferenceEquals(_selectionAnchor, node) ? nextPrimary : _selectionAnchor ?? nextPrimary;
            ApplySelection(list, nextPrimary, false);
            return;
        }

        list.Add(node);
        _selectionAnchor = node;
        ApplySelection(list, node, false);
    }

    public void SelectRange(ScanNodeViewModel target)
    {
        var anchor = ResolveVisibleAnchor(target);
        _selectionAnchor = anchor;

        if (ReferenceEquals(anchor, target))
        {
            ApplySelection([target], target, false);
            return;
        }

        var range = new List<ScanNodeViewModel>();
        var collecting = false;
        var completed = false;

        foreach (var current in EnumerateVisibleNodes())
        {
            if (ReferenceEquals(current, anchor) || ReferenceEquals(current, target))
            {
                range.Add(current);
                if (collecting)
                {
                    completed = true;
                    break;
                }

                collecting = true;
            }
            else if (collecting)
            {
                range.Add(current);
            }
        }

        ApplySelection(completed ? range : [target], target, false);
    }

    public ScanNodeViewModel? MoveRangeSelection(int delta)
    {
        if (delta == 0)
        {
            return SelectedNode;
        }

        var current = SelectedNode is not null && IsNodeVisible(SelectedNode)
            ? SelectedNode
            : Roots.FirstOrDefault();

        if (current is null)
        {
            return null;
        }

        if (_selectionAnchor is null || !IsNodeVisible(_selectionAnchor))
        {
            _selectionAnchor = current;
        }

        var target = FindAdjacentVisibleNode(current, delta < 0);
        SelectRange(target);
        return target;
    }

    public void SelectAllVisible()
    {
        var visible = EnumerateVisibleNodes().ToList();
        if (visible.Count == 0)
        {
            return;
        }

        if (_selectionAnchor is null || !visible.Contains(_selectionAnchor))
        {
            _selectionAnchor = visible[0];
        }

        var primary = SelectedNode is not null && visible.Contains(SelectedNode)
            ? SelectedNode
            : visible[^1];

        ApplySelection(visible, primary, false);
    }

    public void SelectOnRightClick(ScanNodeViewModel node)
    {
        if (_selectedSet.Contains(node))
        {
            if (!ReferenceEquals(SelectedNode, node) || _selectedNodes.Count > 1)
            {
                ApplySelection(_selectedNodes.ToList(), node, false);
            }

            return;
        }

        SelectSingle(node);
    }

    internal IEnumerable<ScanNodeViewModel> EnumerateVisibleNodes()
    {
        foreach (var root in Roots)
        {
            foreach (var node in EnumerateVisibleSubtree(root))
            {
                yield return node;
            }
        }
    }

    internal List<ScanNodeViewModel> ActiveTopLevelNodes()
    {
        var activeRoots = ActiveRootSpaces();
        return ScanTreeEditor.FilterTopLevelNodes(_selectedNodes.Where(n => n.Space is not null && n.Root is not null && activeRoots.Contains(n.Root)));
    }

    private static IEnumerable<ScanNodeViewModel> EnumerateVisibleSubtree(ScanNodeViewModel node)
    {
        yield return node;
        if (!node.IsExpanded)
        {
            yield break;
        }

        foreach (var child in node.RealizedChildren)
        {
            foreach (var descendant in EnumerateVisibleSubtree(child))
            {
                yield return descendant;
            }
        }
    }

    private HashSet<SpaceBase> ActiveRootSpaces()
    {
        return Roots.Select(static r => r.Space).OfType<SpaceBase>().ToHashSet<SpaceBase>(ReferenceEqualityComparer.Instance);
    }

    private ScanNodeViewModel ResolveVisibleAnchor(ScanNodeViewModel fallback)
    {
        if (_selectionAnchor is not null && IsNodeVisible(_selectionAnchor))
        {
            return _selectionAnchor;
        }

        return SelectedNode is not null && IsNodeVisible(SelectedNode) ? SelectedNode : fallback;
    }

    private ScanNodeViewModel FindAdjacentVisibleNode(ScanNodeViewModel current, bool moveUp)
    {
        ScanNodeViewModel? previous = null;
        var foundCurrent = false;

        foreach (var node in EnumerateVisibleNodes())
        {
            if (foundCurrent)
            {
                return node;
            }

            if (ReferenceEquals(node, current))
            {
                if (moveUp)
                {
                    return previous ?? current;
                }

                foundCurrent = true;
            }

            previous = node;
        }

        return current;
    }

    private void InitializeSelectionWiring()
    {
        _nodeFactory.CopyPathRequested += OnCopyPathRequested;
        _nodeFactory.SelectionCount = () => _selectedNodes.Count;
        _nodeFactory.SelectionSummary = () => SelectionSummaryText;
        _nodeFactory.CanIgnoreSelected = () => ActiveTopLevelNodes().Any(static n => n.CanIgnore);
        _nodeFactory.CanUnignoreSelected = () => ActiveTopLevelNodes().Any(static n => n.IsIgnored);
        _nodeFactory.CanMarkSelectedDeleted = () => ActiveTopLevelNodes().Any(static n => n.Space is { State: SpaceState.Added });
        _nodeFactory.CanUnmarkSelectedDeleted = () => ActiveTopLevelNodes().Any(static n => n.HasMarkedSelfOrContents);
        _nodeFactory.MarkSelectedDeleted = MarkSelectedDeleted;
        _nodeFactory.UnmarkSelectedDeleted = UnmarkSelectedDeleted;
        _nodeFactory.MarksChanged += _ => RefreshSelectedNodesState();
        Marks.ItemsDeleted += PruneDeletedSelection;
        Roots.CollectionChanged += (_, _) => RetainSelectionRoots();
    }

    private bool IsNodeVisible(ScanNodeViewModel candidate)
    {
        return EnumerateVisibleNodes().Any(node => ReferenceEquals(node, candidate));
    }

    private void ApplySelection(IReadOnlyList<ScanNodeViewModel> nodes, ScanNodeViewModel? primary, bool updateAnchor)
    {
        IsUpdatingSelection = true;

        try
        {
            if (updateAnchor)
            {
                _selectionAnchor = primary;
            }

            var nextSet = new HashSet<ScanNodeViewModel>(nodes);

            foreach (var previous in _selectedNodes.Where(previous => !nextSet.Contains(previous)))
            {
                previous.IsSelected = false;
            }

            _selectedNodes.Clear();
            _selectedSet.Clear();

            foreach (var item in nodes.Where(_selectedSet.Add))
            {
                _selectedNodes.Add(item);
            }

            var fallback = _selectedNodes.Count > 0 ? _selectedNodes[^1] : null;
            var active = primary is not null && _selectedSet.Contains(primary) ? primary : fallback;

            SelectedNode = active;

            foreach (var item in _selectedNodes)
            {
                item.IsSelected = true;
            }

            UpdateSelectionSummaryAndInspector(active);
            OnPropertyChanged(nameof(SelectedNodes));
            OnPropertyChanged(nameof(HasMultiSelection));
        }
        finally
        {
            IsUpdatingSelection = false;
        }
    }

    private void UpdateSelectionSummaryAndInspector(ScanNodeViewModel? active)
    {
        if (_selectedNodes.Count == 0)
        {
            SelectionSummaryText = string.Empty;
            OnPropertyChanged(nameof(SelectionSummaryText));
            Inspector.Clear();
            return;
        }

        if (_selectedNodes.Count == 1)
        {
            SelectionSummaryText = string.Empty;
            OnPropertyChanged(nameof(SelectionSummaryText));
            Inspector.Show(_selectedNodes[0]);
            _selectedNodes[0].RefreshSelectionState();
            return;
        }

        Inspector.Show(_selectedNodes, active);
        SelectionSummaryText = $"Выбрано: {Inspector.Title} · {Inspector.TotalSizeText}";
        OnPropertyChanged(nameof(SelectionSummaryText));
        RefreshSelectedNodesState();
    }

    private void RefreshSelectedNodesState()
    {
        if (_selectedNodes.Count <= 128)
        {
            foreach (var node in _selectedNodes)
            {
                node.RefreshSelectionState();
            }

            return;
        }

        SelectedNode?.RefreshSelectionState();
    }

    private void OnCopyPathRequested(ScanNodeViewModel node)
    {
        if (node.IsSelected && _selectedNodes.Count > 1)
        {
            Inspector.CopyPaths(ActiveTopLevelNodes());
            return;
        }

        if (node.Space is not null)
        {
            Inspector.CopyPaths([node]);
        }
    }

    private void RetainSelectionRoots()
    {
        var activeRoots = ActiveRootSpaces();
        if (_selectionAnchor is { Root: { } anchorRoot } && !activeRoots.Contains(anchorRoot))
        {
            _selectionAnchor = null;
        }

        RetainSelectionWhere(node => node.Root is not null && activeRoots.Contains(node.Root));
    }

    private void PruneDeletedSelection(IReadOnlySet<SpaceBase> deleted)
    {
        RetainSelectionWhere(node => node.Space is not null && !ScanTreeEditor.IsSelfOrDescendantOfAny(node.Space, deleted));
    }

    private void RetainSelectionWhere(Func<ScanNodeViewModel, bool> predicate)
    {
        if (_selectedNodes.Count == 0)
        {
            return;
        }

        var remaining = _selectedNodes.Where(predicate).ToList();
        if (remaining.Count != _selectedNodes.Count)
        {
            var fallback = remaining.Count > 0 ? remaining[^1] : null;
            var nextPrimary = SelectedNode is not null && remaining.Contains(SelectedNode) ? SelectedNode : fallback;
            ApplySelection(remaining, nextPrimary, true);
        }
    }
}
