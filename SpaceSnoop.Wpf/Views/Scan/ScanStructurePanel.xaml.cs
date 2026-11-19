using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Views.Scan;

public partial class ScanStructurePanel : UserControl
{
    private const double RowPadding = 4;

    private bool _suppressTreeSelectionChange;

    public ScanStructurePanel()
    {
        InitializeComponent();
    }

    public double TreeRowCapacity()
    {
        var chrome = Tree.IsVisible && FindDescendant<ScrollContentPresenter>(Tree) is { ActualHeight: > 0 } viewport
            ? Tree.ActualHeight - viewport.ActualHeight
            : 0;

        var height = TreeRow.ActualHeight - Tree.Margin.Top - chrome;
        var row = TreeRowHeight();

        return row > 0 ? Math.Max(0, height) / row : double.PositiveInfinity;
    }

    public double TreeRowHeight()
    {
        if (Tree.ItemContainerGenerator.ContainerFromIndex(0) is TreeViewItem item
            && item.Template?.FindName("SelectionBd", item) is FrameworkElement { ActualHeight: > 0 } row)
        {
            return row.ActualHeight;
        }

        return (Tree.FontSize * Tree.FontFamily.LineSpacing) + RowPadding;
    }

    internal bool HandleTreeLeftClick(DependencyObject? source, int clickCount, ModifierKeys modifiers)
    {
        if (DataContext is not ScanViewModel vm || FindAncestor<ToggleButton>(source) is not null)
        {
            return false;
        }

        if (FindAncestor<TreeViewItem>(source) is not { DataContext: ScanNodeViewModel node } item)
        {
            return false;
        }

        if ((modifiers & ModifierKeys.Control) != 0)
        {
            vm.ToggleSelection(node);
            FocusTreeItemWithoutSelectionReset(item);
            return true;
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            vm.SelectRange(node);
            FocusTreeItemWithoutSelectionReset(item);
            return true;
        }

        if (clickCount == 1 && (vm.HasMultiSelection || !node.IsPrimarySelected))
        {
            vm.SelectSingle(node);
            FocusTreeItemWithoutSelectionReset(item);
            return true;
        }

        return false;
    }

    internal void HandleTreeRightClick(DependencyObject? source)
    {
        if (FindAncestor<TreeViewItem>(source) is not { } item)
        {
            return;
        }

        if (DataContext is ScanViewModel vm && item.DataContext is ScanNodeViewModel node)
        {
            vm.SelectOnRightClick(node);
            FocusTreeItemWithoutSelectionReset(item);
            return;
        }

        item.IsSelected = true;
    }

    internal bool HandleTreeKey(Key key, ModifierKeys modifiers)
    {
        if (DataContext is not ScanViewModel vm)
        {
            return false;
        }

        switch (modifiers, key)
        {
            case (ModifierKeys.None, Key.Delete):
                vm.MarkSelectedDeleted();
                return true;

            case (ModifierKeys.None, Key.Space):
                vm.ToggleSelectedDeleted();
                return true;

            case (ModifierKeys.Control, Key.A):
                vm.SelectAllVisible();
                return true;

            case (ModifierKeys.Shift, Key.Up):
                MoveRangeAndFocus(vm, -1);
                return true;

            case (ModifierKeys.Shift, Key.Down):
                MoveRangeAndFocus(vm, 1);
                return true;

            default:
                return false;
        }
    }

    private void MoveRangeAndFocus(ScanViewModel vm, int delta)
    {
        if (vm.MoveRangeSelection(delta) is { } target && FindContainer(Tree, target) is { } container)
        {
            container.BringIntoView();
            FocusTreeItemWithoutSelectionReset(container);
        }
    }

    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (!_suppressTreeSelectionChange
            && DataContext is ScanViewModel { IsRefreshingVisibility: false, IsUpdatingSelection: false } vm)
        {
            var next = e.NewValue as ScanNodeViewModel;

            if (!ReferenceEquals(next, vm.SelectedNode))
            {
                vm.SelectSingle(next);
            }
        }
    }

    private void OnTreeLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (HandleTreeLeftClick(e.OriginalSource as DependencyObject, e.ClickCount, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    private void OnTreeRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        HandleTreeRightClick(e.OriginalSource as DependencyObject);
    }

    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleTreeKey(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    private void FocusTreeItemWithoutSelectionReset(TreeViewItem item)
    {
        _suppressTreeSelectionChange = true;

        try
        {
            item.Focus();

            if (item.DataContext is ScanNodeViewModel node && item.IsSelected != node.IsPrimarySelected)
            {
                item.SetCurrentValue(TreeViewItem.IsSelectedProperty, node.IsPrimarySelected);
            }

            if (DataContext is ScanViewModel { SelectedNode: { } primary }
                && FindContainer(Tree, primary) is { IsSelected: false } primaryItem)
            {
                primaryItem.SetCurrentValue(TreeViewItem.IsSelectedProperty, true);
            }
        }
        finally
        {
            _suppressTreeSelectionChange = false;
        }
    }

    private static TreeViewItem? FindContainer(ItemsControl host, ScanNodeViewModel target)
    {
        var chain = new Stack<ScanNodeViewModel>();

        for (var current = target; current is not null; current = current.ParentNode)
        {
            chain.Push(current);
        }

        TreeViewItem? found = null;

        while (chain.TryPop(out var step))
        {
            if (host.ItemContainerGenerator.ContainerFromItem(step) is not TreeViewItem container)
            {
                return null;
            }

            found = container;
            host = container;
        }

        return found;
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null and not T)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        return current as T;
    }

    private static T? FindDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if ((child as T ?? FindDescendant<T>(child)) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
