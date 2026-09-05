using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace SpaceSnoop.Wpf.ViewModels.Overview;

public sealed partial class OverviewRowsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly Func<bool> _isBusy;
    private readonly Func<bool> _suppressReload;
    private readonly Action _reload;
    private readonly bool _suppressPersist;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private OverviewSortField _sortField = AppDefaults.OverviewSortDefault;

    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private bool _groupUnchanged = AppDefaults.OverviewGroupUnchangedDefault;

    internal OverviewRowsViewModel(ISettingsStore settings, Func<bool> isBusy, Func<bool> suppressReload, Action reload)
    {
        _settings = settings;
        _isBusy = isBusy;
        _suppressReload = suppressReload;
        _reload = reload;

        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = FilterRow;

        _suppressPersist = true;
        SortField = _settings.GetEnum(SettingsKeys.OverviewSort, AppDefaults.OverviewSortDefault);
        SortDescending = _settings.GetBool(SettingsKeys.OverviewSortDesc);
        GroupUnchanged = _settings.GetBool(SettingsKeys.OverviewGroupUnchanged, AppDefaults.OverviewGroupUnchangedDefault);
        _suppressPersist = false;

        UpdateSortAndGroup();
        _settings.Changed += OnSettingsChanged;
    }

    public ObservableCollection<OverviewRowViewModel> Rows { get; } = [];

    public ICollectionView RowsView { get; }

    public bool HasRows => Rows.Count > 0;

    public bool SearchTextEmpty => string.IsNullOrWhiteSpace(SearchText);

    public bool NoMatches => Rows.Count > 0 && RowsView.IsEmpty;

    public double ScrollOffset { get; set; }

    internal void NotifyRowsChanged()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(NoMatches));
    }

    internal void RefreshView()
    {
        RowsView.Refresh();
        OnPropertyChanged(nameof(NoMatches));
    }

    private void OnSettingsChanged(object? sender, string key)
    {
        if (key == SettingsKeys.ScheduleProfiles && !_isBusy() && !_suppressReload())
        {
            _reload();
        }
    }

    private bool FilterRow(object item)
    {
        if (item is not OverviewRowViewModel row)
        {
            return false;
        }

        var query = SearchText.Trim();

        if (query.Length == 0)
        {
            return true;
        }

        return row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
               || row.Left.Contains(query, StringComparison.OrdinalIgnoreCase)
               || row.Right.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateSortAndGroup()
    {
        using (RowsView.DeferRefresh())
        {
            RowsView.GroupDescriptions.Clear();
            RowsView.SortDescriptions.Clear();

            if (GroupUnchanged)
            {
                RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(OverviewRowViewModel.GroupKey)));
                RowsView.SortDescriptions.Add(new(nameof(OverviewRowViewModel.GroupOrder), ListSortDirection.Ascending));
            }

            var direction = SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;

            switch (SortField)
            {
                case OverviewSortField.Name:
                    RowsView.SortDescriptions.Add(new(nameof(OverviewRowViewModel.Name), direction));
                    break;

                case OverviewSortField.Differences:
                    RowsView.SortDescriptions.Add(new(nameof(OverviewRowViewModel.DiffCount), direction));
                    break;

                case OverviewSortField.Date:
                    RowsView.SortDescriptions.Add(new(nameof(OverviewRowViewModel.NewestModified), direction));
                    break;

                case OverviewSortField.Freshness:
                    RowsView.SortDescriptions.Add(new(nameof(OverviewRowViewModel.FreshnessOrder), ListSortDirection.Ascending));
                    RowsView.SortDescriptions.Add(new(nameof(OverviewRowViewModel.FreshnessLead),
                        SortDescending ? ListSortDirection.Ascending : ListSortDirection.Descending));
                    break;
            }
        }

        OnPropertyChanged(nameof(NoMatches));
    }

    private void Persist(string key, string value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.SetValue(key, value);
    }

    partial void OnSearchTextChanged(string value)
    {
        RowsView.Refresh();
        OnPropertyChanged(nameof(SearchTextEmpty));
        OnPropertyChanged(nameof(NoMatches));
    }

    partial void OnSortFieldChanged(OverviewSortField value)
    {
        Persist(SettingsKeys.OverviewSort, value.ToString());
        UpdateSortAndGroup();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        Persist(SettingsKeys.OverviewSortDesc, value ? "true" : "false");
        UpdateSortAndGroup();
    }

    partial void OnGroupUnchangedChanged(bool value)
    {
        Persist(SettingsKeys.OverviewGroupUnchanged, value ? "true" : "false");
        UpdateSortAndGroup();
    }
}
