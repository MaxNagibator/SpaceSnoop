using System.ComponentModel;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Views;

public partial class ScanView : UserControl, IView<ScanViewModel>
{
    public const int MinTreeRows = 6;

    private const double RailWidth = 46;

    private const double MinPanelWidth = ScanInspectorViewModel.MinInspectorWidth;

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact),
        typeof(bool),
        typeof(ScanView),
        new(false));

    private ScanViewModel? _vm;
    private bool _hooked;
    private bool _fitting;
    private bool _fitQueued;

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        private set => SetValue(IsCompactProperty, value);
    }

    public ScanView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        ContentArea.IsVisibleChanged += (_, _) => QueueFit();
        Structure.HeaderBar.SizeChanged += (_, _) => QueueFit();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Hook();
        ApplyInspectorLayout();
        QueueFit();
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

        QueueFit();
    }

    private void QueueFit()
    {
        if (_fitting || _fitQueued)
        {
            return;
        }

        _fitQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, FitUpperBlocks);
    }

    private void FitUpperBlocks()
    {
        _fitQueued = false;

        if (!IsLoaded || !ContentArea.IsVisible)
        {
            return;
        }

        _fitting = true;

        try
        {
            IsCompact = false;
            UpdateLayout();

            if (Structure.TreeRowCapacity() < MinTreeRows)
            {
                IsCompact = true;
                UpdateLayout();
            }

            _vm?.Inspector.SetCompact(IsCompact);
        }
        finally
        {
            _fitting = false;
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
        var splitter = double.IsNaN(InspectorSplitter.Width) ? SplitterColumn.ActualWidth : InspectorSplitter.Width;
        var free = ContentArea.ActualWidth - StructureColumn.MinWidth - splitter;

        return free > MinPanelWidth ? free : MinPanelWidth;
    }
}
