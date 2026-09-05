using System.ComponentModel;
using System.Windows.Controls.Primitives;

namespace SpaceSnoop.Wpf.Views;

public partial class ScanView : UserControl, IView<ScanViewModel>
{
    private const double RailWidth = 46;

    private const double MinPanelWidth = ScanInspectorViewModel.MinInspectorWidth;

    private ScanViewModel? _vm;
    private bool _hooked;

    public ScanView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Hook();
        ApplyInspectorLayout();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unhook();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Unhook();
        _vm = e.NewValue as ScanViewModel;

        if (IsLoaded)
        {
            Hook();
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

    private void OnContentAreaSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            ApplyInspectorLayout();
        }
    }

    private void OnInspectorSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        var inspector = _vm?.Inspector;

        if (inspector is null || inspector.IsInspectorCollapsed)
        {
            return;
        }

        inspector.SetInspectorWidth(InspectorColumn.ActualWidth);

        ApplyInspectorLayout();
    }

    private void Hook()
    {
        if (_hooked || _vm is null)
        {
            return;
        }

        _vm.Inspector.PropertyChanged += OnInspectorPropertyChanged;
        _hooked = true;
    }

    private void Unhook()
    {
        if (!_hooked || _vm is null)
        {
            return;
        }

        _vm.Inspector.PropertyChanged -= OnInspectorPropertyChanged;
        _hooked = false;
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
        }
        else
        {
            InspectorColumn.SetCurrentValue(ColumnDefinition.MinWidthProperty, MinPanelWidth);
            InspectorColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(Math.Min(_vm.Inspector.InspectorWidth, AvailableInspectorWidth())));
        }
    }

    private double AvailableInspectorWidth()
    {
        var free = ContentArea.ActualWidth - StructureColumn.MinWidth - SplitterColumn.ActualWidth;

        return free > MinPanelWidth ? free : MinPanelWidth;
    }
}
