using System.ComponentModel;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Views;

public partial class ScanView : UserControl, IView<ScanViewModel>
{
    private const double RailWidth = 46;

    private const double MinPanelWidth = ScanInspectorViewModel.MinInspectorWidth;

    private ScanViewModel? _vm;

    public ScanView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyInspectorLayout();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.Inspector.PropertyChanged -= OnInspectorPropertyChanged;
        }

        _vm = e.NewValue as ScanViewModel;

        if (_vm is not null)
        {
            _vm.Inspector.PropertyChanged += OnInspectorPropertyChanged;
            ApplyInspectorLayout();
        }
    }

    private void OnInspectorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScanInspectorViewModel.IsInspectorCollapsed))
        {
            ApplyInspectorLayout();
        }
    }

    private void OnInspectorSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_vm is null || _vm.Inspector.IsInspectorCollapsed)
        {
            return;
        }

        _vm.Inspector.SetInspectorWidth(InspectorColumn.ActualWidth);

        ApplyInspectorLayout();
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

    private void ApplyInspectorLayout()
    {
        if (_vm is null)
        {
            return;
        }

        if (_vm.Inspector.IsInspectorCollapsed)
        {
            InspectorColumn.SetCurrentValue(ColumnDefinition.MinWidthProperty, RailWidth);
            InspectorColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(RailWidth));
            SplitterColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(0));
        }
        else
        {
            InspectorColumn.SetCurrentValue(ColumnDefinition.MinWidthProperty, MinPanelWidth);
            InspectorColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(_vm.Inspector.InspectorWidth));
            SplitterColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(6));
        }
    }
}
