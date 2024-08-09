namespace SpaceSnoop;

public partial class MainForm
{
    private bool _isSorting;

    private void InitializeSorting()
    {
        _sortModeComboBox.SelectedIndexChanged += OnSortModeChanged;
        _invertSortCheckBox.CheckedChanged += OnInvertSortCheckBoxChanged;

        object[] sorterModes =
        [
            SorterMode.ByName,
            SorterMode.BySize,
            SorterMode.ByDate,
            SorterMode.ByLastAccessTime
        ];

        _sortModeComboBox.Items.AddRange(sorterModes);
        _sortModeComboBox.SelectedIndex = 1;

        _invertSortCheckBox.Checked = true;
    }

    private void FinalizeSorting()
    {
        _sortModeComboBox.SelectedIndexChanged -= OnSortModeChanged;
        _invertSortCheckBox.CheckedChanged -= OnInvertSortCheckBoxChanged;
    }

    private void OnSortModeChanged(object? sender, EventArgs e)
    {
        SortNodes();
    }

    private void OnInvertSortCheckBoxChanged(object? sender, EventArgs e)
    {
        // TODO Крайне неудачное решение
        NodeSorterBase.SetInversion(_invertSortCheckBox.Checked);
        SortNodes();
    }

    private void SortNodes()
    {
        if (_sortModeComboBox.SelectedItem is not SorterMode selectedSortMode)
        {
            return;
        }

        _isSorting = true;
        _directoriesTreeView.TreeViewNodeSorter = selectedSortMode.Comparer;
        _directoriesTreeView.Sort();
        _isSorting = false;
    }
}
