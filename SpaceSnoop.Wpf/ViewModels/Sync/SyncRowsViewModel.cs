using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncRowsViewModel : ObservableObject, ISyncRowHost
{
    private readonly ISettingsStore _settings;
    private readonly OperationPreferences _operations;
    private readonly AgentPreferences _agent;
    private readonly Func<FileComparison, Task> _compareContent;
    private readonly Action<SyncNodeViewModel> _askAgent;
    private readonly HashSet<DirectoryComparison> _collapsed = [];
    private readonly HashSet<string> _collapsedSubGroups = new(StringComparer.OrdinalIgnoreCase);

    private ComparisonResult? _result;
    private Dictionary<object, SyncOutcome> _outcomes = [];
    private Dictionary<DirectoryComparison, (long Left, long Right)>? _dirSizeCache;
    private bool _suppressPersist;
    private bool _gitGroupExpanded;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _showIdentical;

    [ObservableProperty]
    private bool _showSizes = AppDefaults.SyncShowSizesDefault;

    [ObservableProperty]
    private bool _showModified = AppDefaults.SyncShowModifiedDefault;

    [ObservableProperty]
    private bool _blankAbsent;

    [ObservableProperty]
    private bool _hideApplied;

    [ObservableProperty]
    private bool _flatView;

    [ObservableProperty]
    private SyncSortField _rowSort = AppDefaults.SyncFlatSortDefault;

    [ObservableProperty]
    private bool _rowSortDescending;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public SyncRowsViewModel(
        ISettingsStore settings,
        OperationPreferences operations,
        AgentPreferences agent,
        Func<FileComparison, Task> compareContent,
        Action<SyncNodeViewModel> askAgent)
    {
        _settings = settings;
        _operations = operations;
        _agent = agent;
        _compareContent = compareContent;
        _askAgent = askAgent;
        LoadSettings();
    }

    public event Action? ActionsChanged;

    public RangeObservableCollection<SyncNodeViewModel> Rows { get; } = [];

    public bool ChatEnabled => _agent.Enabled;

    public bool ShowApplied
    {
        get => !HideApplied;
        set => HideApplied = !value;
    }

    public bool SearchTextEmpty => string.IsNullOrWhiteSpace(SearchText);

    public bool SortByPath => RowSort is SyncSortField.Path or SyncSortField.None;

    public bool SortBySize => RowSort == SyncSortField.Size;

    public bool SortByModified => RowSort == SyncSortField.Modified;

    public PackIconLucideKind SortDirectionIconKind => RowSortDescending
        ? PackIconLucideKind.ArrowDown
        : PackIconLucideKind.ArrowUp;

    public void ToggleExpand(DirectoryComparison dir)
    {
        if (!_collapsed.Remove(dir))
        {
            _collapsed.Add(dir);
        }

        Rebuild();
    }

    public void ToggleGroup(string? key)
    {
        if (key is null)
        {
            _gitGroupExpanded = !_gitGroupExpanded;
        }
        else if (!_collapsedSubGroups.Remove(key))
        {
            _collapsedSubGroups.Add(key);
        }

        Rebuild();
    }

    public void CollapseSubtree(DirectoryComparison dir)
    {
        SyncRowsProjector.AddCollapsed(_collapsed, dir);
        Rebuild();
    }

    public void ExpandSubtree(DirectoryComparison dir)
    {
        SyncRowsProjector.RemoveCollapsed(_collapsed, dir);
        Rebuild();
    }

    public void ApplyToSubtree(DirectoryComparison dir, SyncAction action)
    {
        ApplyActionRecursive(dir, action);
        Rebuild();
        ActionsChanged?.Invoke();
    }

    public void NotifyActionsChanged()
    {
        foreach (var row in Rows)
        {
            row.RefreshSubtreeAction();
        }

        ActionsChanged?.Invoke();
    }

    public Task CompareContentAsync(FileComparison file)
    {
        return _compareContent(file);
    }

    public void AskAgentAbout(SyncNodeViewModel node)
    {
        _askAgent(node);
    }

    [RelayCommand]
    public void CollapseAll()
    {
        if (_result is null)
        {
            return;
        }

        SyncRowsProjector.CollapseAllDirectories(_collapsed, _result.Root);
        Rebuild();
    }

    [RelayCommand]
    public void ExpandAll()
    {
        if (_result is null)
        {
            return;
        }

        _collapsed.Clear();
        Rebuild();
    }

    internal void Update(
        ComparisonResult? result,
        Dictionary<object, SyncOutcome> outcomes,
        Dictionary<DirectoryComparison, (long Left, long Right)>? dirSizeCache,
        bool collapseAll = false)
    {
        _result = result;
        _outcomes = outcomes;
        _dirSizeCache = dirSizeCache;
        HasResult = result is not null;

        if (result is null)
        {
            _collapsed.Clear();
        }
        else if (collapseAll)
        {
            SyncRowsProjector.CollapseAllDirectories(_collapsed, result.Root);
        }

        Rebuild();
    }

    internal void Rebuild()
    {
        var result = _result;

        if (result is null)
        {
            Rows.ReplaceAll([]);
            return;
        }

        var request = new SyncRowsRequest
        {
            Result = result,
            FlatView = FlatView,
            SearchText = SearchText,
            ShowIdentical = ShowIdentical,
            HideApplied = HideApplied,
            RowSort = RowSort,
            RowSortDescending = RowSortDescending,
            Outcomes = _outcomes,
            DirSizeCache = _dirSizeCache,
            Collapsed = _collapsed,
            CollapsedSubGroups = _collapsedSubGroups,
            GitGroupExpanded = _gitGroupExpanded,
            GroupFolders = _operations.GroupFolders,
        };

        Rows.ReplaceAll(SyncRowsProjector.Build(request, this));
    }

    private static void ApplyActionRecursive(DirectoryComparison dir, SyncAction action)
    {
        if (dir.Status is ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly)
        {
            dir.Action = DirActionFor(dir, action);
        }

        foreach (var file in dir.Files)
        {
            if (file.Status != ComparisonStatus.Identical)
            {
                file.Action = action;
            }
        }

        foreach (var sub in dir.SubDirectories)
        {
            ApplyActionRecursive(sub, action);
        }
    }

    private static SyncAction DirActionFor(DirectoryComparison dir, SyncAction requested)
    {
        return dir.Status == ComparisonStatus.LeftOnly
            ? requested switch
            {
                SyncAction.CopyToRight => SyncAction.CopyToRight,
                SyncAction.DeleteLeft => SyncAction.DeleteLeft,
                SyncAction.Skip => SyncAction.Skip,
                _ => dir.Action,
            }
            : requested switch
            {
                SyncAction.CopyToLeft => SyncAction.CopyToLeft,
                SyncAction.DeleteRight => SyncAction.DeleteRight,
                SyncAction.Skip => SyncAction.Skip,
                _ => dir.Action,
            };
    }

    [RelayCommand]
    private void SortByColumn(SyncSortField field)
    {
        if (RowSort == field)
        {
            RowSortDescending = !RowSortDescending;
            return;
        }

        RowSort = field;
        RowSortDescending = field is SyncSortField.Size or SyncSortField.Modified;
    }

    partial void OnShowIdenticalChanged(bool value)
    {
        Persist(SettingsKeys.SyncShowIdentical, value ? "true" : "false");
        RebuildIfResult();
    }

    partial void OnShowSizesChanged(bool value)
    {
        Persist(SettingsKeys.SyncShowSizes, value ? "true" : "false");
    }

    partial void OnShowModifiedChanged(bool value)
    {
        Persist(SettingsKeys.SyncShowModified, value ? "true" : "false");
    }

    partial void OnBlankAbsentChanged(bool value)
    {
        Persist(SettingsKeys.SyncBlankAbsent, value ? "true" : "false");
        RebuildIfResult();
    }

    partial void OnHideAppliedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowApplied));
        Persist(SettingsKeys.SyncHideApplied, value ? "true" : "false");
        RebuildIfResult();
    }

    partial void OnFlatViewChanged(bool value)
    {
        Persist(SettingsKeys.SyncFlatView, value ? "true" : "false");
        RebuildIfResult();
    }

    partial void OnRowSortChanged(SyncSortField value)
    {
        Persist(SettingsKeys.SyncFlatSort, value.ToString());
        NotifySortChanged();
        RebuildIfResult();
    }

    partial void OnRowSortDescendingChanged(bool value)
    {
        Persist(SettingsKeys.SyncFlatSortDesc, value ? "true" : "false");
        NotifySortChanged();
        RebuildIfResult();
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(SearchTextEmpty));
        RebuildIfResult();
    }

    private void RebuildIfResult()
    {
        if (_result is not null)
        {
            Rebuild();
        }
    }

    private void NotifySortChanged()
    {
        OnPropertyChanged(nameof(SortByPath));
        OnPropertyChanged(nameof(SortBySize));
        OnPropertyChanged(nameof(SortByModified));
        OnPropertyChanged(nameof(SortDirectionIconKind));
    }

    private void LoadSettings()
    {
        _suppressPersist = true;

        ShowIdentical = _settings.GetBool(SettingsKeys.SyncShowIdentical);
        ShowSizes = _settings.GetBool(SettingsKeys.SyncShowSizes, AppDefaults.SyncShowSizesDefault);
        ShowModified = _settings.GetBool(SettingsKeys.SyncShowModified, AppDefaults.SyncShowModifiedDefault);
        BlankAbsent = _settings.GetBool(SettingsKeys.SyncBlankAbsent);
        HideApplied = _settings.GetBool(SettingsKeys.SyncHideApplied);
        FlatView = _settings.GetBool(SettingsKeys.SyncFlatView);
        RowSort = _settings.GetEnum(SettingsKeys.SyncFlatSort, AppDefaults.SyncFlatSortDefault);
        RowSortDescending = _settings.GetBool(SettingsKeys.SyncFlatSortDesc);

        _suppressPersist = false;
    }

    private void Persist(string key, string value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.SetValue(key, value);
    }
}
