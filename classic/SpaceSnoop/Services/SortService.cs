namespace SpaceSnoop.Services;

public sealed class SortService : IDisposable
{
    private ComboBox? _sortModeComboBox;
    private CheckBox? _invertSortCheckBox;
    private TreeView? _directoriesTreeView;

    public bool IsSorting { get; private set; }

    public void Dispose()
    {
        if (_sortModeComboBox != null)
        {
            _sortModeComboBox.SelectedIndexChanged -= OnSortModeChanged;
        }

        if (_invertSortCheckBox != null)
        {
            _invertSortCheckBox.CheckedChanged -= OnInvertSortCheckBoxChanged;
        }

        GC.SuppressFinalize(this);
    }

    public void Initialize(ComboBox sortModeComboBox, CheckBox invertSortCheckBox, TreeView directoriesTreeView)
    {
        _sortModeComboBox = sortModeComboBox;
        _invertSortCheckBox = invertSortCheckBox;
        _directoriesTreeView = directoriesTreeView;

        _sortModeComboBox.SelectedIndexChanged += OnSortModeChanged;
        _invertSortCheckBox.CheckedChanged += OnInvertSortCheckBoxChanged;

        object[] sorterModes =
        [
            SorterMode.ByName,
            SorterMode.BySize,
            SorterMode.ByDate,
            SorterMode.ByLastAccessTime,
        ];

        _sortModeComboBox.Items.AddRange(sorterModes);
        _sortModeComboBox.SelectedIndex = 1;

        _invertSortCheckBox.Checked = true;
    }

    public void SortNodes()
    {
        if (_sortModeComboBox?.SelectedItem is not SorterMode selectedSortMode || _directoriesTreeView == null)
        {
            return;
        }

        IsSorting = true;
        _directoriesTreeView.TreeViewNodeSorter = selectedSortMode.Comparer;
        _directoriesTreeView.Sort();
        IsSorting = false;
    }

    private void OnSortModeChanged(object? sender, EventArgs e)
    {
        SortNodes();
    }

    private void OnInvertSortCheckBoxChanged(object? sender, EventArgs e)
    {
        NodeSorterBase.SetInversion(_invertSortCheckBox!.Checked);
        SortNodes();
    }
}
