using System.Windows.Input;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Views.Controls;

public partial class ScanStructurePanel : UserControl
{
    public ScanStructurePanel()
    {
        InitializeComponent();
    }

    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ScanViewModel vm)
        {
            vm.SelectedNode = e.NewValue as ScanNodeViewModel;
        }
    }

    private void OnTreeRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            item.IsSelected = true;
        }
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
}
