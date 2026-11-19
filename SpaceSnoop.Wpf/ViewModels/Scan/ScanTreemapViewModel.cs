namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanTreemapViewModel : ObservableObject
{
    private readonly IReadOnlyList<ScanNodeViewModel> _roots;
    private readonly List<ScanNodeViewModel> _treemapPath = [];

    [ObservableProperty]
    private ScanNodeViewModel? _treemapRoot;

    [ObservableProperty]
    private bool _hasTreemapTiles;

    [ObservableProperty]
    private bool _treemapTruncated;

    [ObservableProperty]
    private string _treemapTruncatedText = string.Empty;

    public event Action<ScanNodeViewModel>? DrilledInto;

    public RangeObservableCollection<ScanNodeViewModel> TreemapTiles { get; } = [];

    public RangeObservableCollection<TreemapCrumb> TreemapBreadcrumbs { get; } = [];

    public ScanTreemapViewModel(IReadOnlyList<ScanNodeViewModel> roots)
    {
        _roots = roots;
    }

    internal void SetRoot(ScanNodeViewModel root)
    {
        _treemapPath.Clear();
        _treemapPath.Add(root);
        TreemapRoot = root;
        RebuildBreadcrumbs();
        RebuildTiles();
    }

    internal void RebuildTiles()
    {
        if (TreemapRoot is null)
        {
            TreemapTiles.ReplaceAll([]);
            HasTreemapTiles = false;
            TreemapTruncated = false;
            TreemapTruncatedText = string.Empty;
            return;
        }

        TreemapRoot.EnsureLoaded();

        var shown = ScanTopSelector.SelectTopWeightedNodes(
            TreemapRoot.Children,
            AppDefaults.TreemapTileLimit,
            out var visibleCount);

        TreemapTiles.ReplaceAll(shown);
        HasTreemapTiles = shown.Length > 0;

        var hidden = visibleCount - shown.Length;
        TreemapTruncated = hidden > 0;
        TreemapTruncatedText = hidden > 0
            ? $"Показаны крупнейшие {shown.Length} из {visibleCount}"
            : string.Empty;
    }

    internal void RefreshVisibility(ScanVisibility visibility, IReadOnlySet<SpaceBase> affected)
    {
        if (TreemapRoot?.Space is not { } current || (!affected.Contains(current) && !visibility.IsIgnored(current)))
        {
            return;
        }

        var hidden = _treemapPath.Select(static node => node.Space)
            .OfType<SpaceBase>()
            .Where(visibility.IsIgnored)
            .ToHashSet();

        RefreshAfterDeletion(hidden);
    }

    internal void RefreshAfterDeletion(HashSet<SpaceBase> deletedSet)
    {
        if (_treemapPath.Count == 0)
        {
            return;
        }

        var cut = -1;

        for (var i = 0; i < _treemapPath.Count; i++)
        {
            var space = _treemapPath[i].Space;

            if (space is null || deletedSet.Contains(space))
            {
                cut = i;
                break;
            }
        }

        if (cut == 0)
        {
            var fallback = _roots.FirstOrDefault();

            if (fallback is null)
            {
                _treemapPath.Clear();
                TreemapRoot = null;
                TreemapBreadcrumbs.ReplaceAll([]);
                RebuildTiles();
            }
            else
            {
                SetRoot(fallback);
            }

            return;
        }

        if (cut > 0)
        {
            _treemapPath.RemoveRange(cut, _treemapPath.Count - cut);
            TreemapRoot = _treemapPath[^1];
            RebuildBreadcrumbs();
        }

        RebuildTiles();
    }

    [RelayCommand]
    private void DrillInto(ScanNodeViewModel? node)
    {
        if (node is null || !node.IsDirectory || !node.HasChildren)
        {
            return;
        }

        _treemapPath.Add(node);
        TreemapRoot = node;
        RebuildBreadcrumbs();
        RebuildTiles();
        DrilledInto?.Invoke(node);
    }

    [RelayCommand]
    private void DrillToCrumb(ScanNodeViewModel? node)
    {
        if (node is null)
        {
            return;
        }

        var index = _treemapPath.IndexOf(node);

        if (index < 0)
        {
            return;
        }

        _treemapPath.RemoveRange(index + 1, _treemapPath.Count - index - 1);
        TreemapRoot = node;
        RebuildBreadcrumbs();
        RebuildTiles();
    }

    private void RebuildBreadcrumbs()
    {
        var crumbs = new TreemapCrumb[_treemapPath.Count];

        for (var i = 0; i < _treemapPath.Count; i++)
        {
            crumbs[i] = new(_treemapPath[i], i > 0);
        }

        TreemapBreadcrumbs.ReplaceAll(crumbs);
    }
}

public sealed record TreemapCrumb(ScanNodeViewModel Node, bool ShowSeparator);
