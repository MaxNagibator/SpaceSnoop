using MahApps.Metro.IconPacks;
using SpaceSnoop.Wpf.Converters;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Views.Scan;

public sealed class TreemapView : FrameworkElement
{
    private const double LabelMinWidth = 46;
    private const double LabelMinHeight = 32;
    private const double Gap = 3;
    private const double CornerRadius = 3;
    private const double Padding = 5;
    private const double IconSize = 12;
    private const double IconGap = 5;
    private const double MarkerSize = 9;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(TreemapView),
            new FrameworkPropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty IntensityProperty =
        DependencyProperty.Register(nameof(Intensity),
            typeof(double),
            typeof(TreemapView),
            new FrameworkPropertyMetadata(AppDefaults.IntensityDefault, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem),
            typeof(ScanNodeViewModel),
            typeof(TreemapView),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DrillCommandProperty =
        DependencyProperty.Register(nameof(DrillCommand), typeof(ICommand), typeof(TreemapView));

    public static readonly DependencyProperty PerformanceProperty =
        DependencyProperty.Register(nameof(Performance), typeof(PerformanceMonitor), typeof(TreemapView));

    public static readonly DependencyProperty NodeContextMenuProperty =
        DependencyProperty.Register(nameof(NodeContextMenu), typeof(ContextMenu), typeof(TreemapView));

    public static readonly DependencyProperty NodeTooltipTemplateProperty =
        DependencyProperty.Register(nameof(NodeTooltipTemplate), typeof(DataTemplate), typeof(TreemapView));

    public static readonly DependencyProperty TooltipStyleProperty =
        DependencyProperty.Register(nameof(TooltipStyle), typeof(Style), typeof(TreemapView),
            new FrameworkPropertyMetadata(null, OnTooltipStyleChanged));

    private static readonly SolidColorBrush LabelBrush = Frozen(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly SolidColorBrush SubLabelBrush = Frozen(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly SolidColorBrush MarkerBrush = Frozen(Color.FromArgb(0xCC, 0x1A, 0x1A, 0x1A));
    private static readonly Brush CushionBrush = FrozenCushion();
    private static readonly Geometry FolderIcon = ParseIcon(PackIconLucideKind.Folder);
    private static readonly Geometry FileIcon = ParseIcon(PackIconLucideKind.File);
    private static readonly Geometry BeakUp = FrozenGeometry("M 0,8 L 8,0 L 16,8");
    private static readonly Geometry BeakDown = FrozenGeometry("M 0,0 L 8,8 L 16,0");
    private static readonly Geometry BeakLeft = FrozenGeometry("M 8,0 L 0,8 L 8,16");
    private static readonly Geometry BeakRight = FrozenGeometry("M 0,0 L 8,8 L 0,16");

    private readonly FrameworkElement _menuHost = new();
    private readonly List<INotifyPropertyChanged> _subscribed = [];
    private readonly ToolTip _toolTip = new() { Placement = PlacementMode.Custom };

    private (Rect Rect, ScanNodeViewModel Node)[] _tiles = [];
    private ScanNodeViewModel? _hover;
    private Path? _beak;
    private Point _cursor;
    private bool _nudge;
    private bool _isLoaded;

    public TreemapView()
    {
        AddVisualChild(_menuHost);
        _toolTip.PlacementTarget = this;
        _toolTip.CustomPopupPlacementCallback = PlaceTooltip;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public IEnumerable? ItemsSource
    {
        get => ReadDependencyValue<IEnumerable>(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public double Intensity
    {
        get => (double)GetValue(IntensityProperty);
        set => SetValue(IntensityProperty, value);
    }

    public ScanNodeViewModel? SelectedItem
    {
        get => (ScanNodeViewModel?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public ICommand? DrillCommand
    {
        get => ReadDependencyValue<ICommand>(DrillCommandProperty);
        set => SetValue(DrillCommandProperty, value);
    }

    public PerformanceMonitor? Performance
    {
        get => ReadDependencyValue<PerformanceMonitor>(PerformanceProperty);
        set => SetValue(PerformanceProperty, value);
    }

    public ContextMenu? NodeContextMenu
    {
        get => (ContextMenu?)GetValue(NodeContextMenuProperty);
        set => SetValue(NodeContextMenuProperty, value);
    }

    public DataTemplate? NodeTooltipTemplate
    {
        get => (DataTemplate?)GetValue(NodeTooltipTemplateProperty);
        set => SetValue(NodeTooltipTemplateProperty, value);
    }

    public Style? TooltipStyle
    {
        get => (Style?)GetValue(TooltipStyleProperty);
        set => SetValue(TooltipStyleProperty, value);
    }

    protected override int VisualChildrenCount => 1;

    public ToolTip? ShowTooltipForAutomation(Point point)
    {
        var node = HitTest(point);

        if (node is null)
        {
            return null;
        }

        _cursor = point;
        _hover = node;
        ShowTooltip(node);

        return _toolTip.IsOpen ? _toolTip : null;
    }

    public void HideTooltipForAutomation()
    {
        _hover = null;
        _toolTip.IsOpen = false;
    }

    protected override void OnRender(DrawingContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var width = ActualWidth;
        var height = ActualHeight;

        context.DrawRectangle(Brushes.Transparent, null, new(0, 0, width, height));

        var nodes = Nodes();
        var weights = new double[nodes.Count];

        for (var i = 0; i < nodes.Count; i++)
        {
            weights[i] = nodes[i].Weight;
        }

        var inset = Gap / 2;
        var layout = TreemapLayout.Squarify(weights, Math.Max(0, width - Gap), Math.Max(0, height - Gap));
        var tiles = new (Rect, ScanNodeViewModel)[nodes.Count];

        var style = new TileStyle(ResourcePen("Fg.Primary", 1.5),
            ResourcePen("State.Error", 1.5),
            TryFindResource("Font.Sans") as FontFamily,
            Intensity,
            FontScaleManager.Current);

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var bounds = new Rect(layout[i].X + inset, layout[i].Y + inset, layout[i].Width, layout[i].Height);
            tiles[i] = (bounds, node);

            DrawTile(context, node, Deflate(bounds, inset), style);
        }

        _tiles = tiles;

        Performance?.ReportRender(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
    }

    private void DrawTile(DrawingContext context, ScanNodeViewModel node, Rect tile, TileStyle style)
    {
        if (tile.Width <= 0 || tile.Height <= 0)
        {
            return;
        }

        var fill = new SolidColorBrush(HeatColor.From(node.Fraction, style.Intensity));

        if (node.IsMarkedDeleted)
        {
            fill.Opacity = 0.5;
        }

        fill.Freeze();

        var pen = ReferenceEquals(node, SelectedItem) ? style.Selection
            : node.IsMarkedDeleted ? style.Deleted
            : null;

        var radius = node.IsDirectory ? CornerRadius : 0;

        context.DrawRoundedRectangle(fill, null, tile, radius, radius);

        if (node is { IsDirectory: true, IsMarkedDeleted: false })
        {
            context.DrawRoundedRectangle(CushionBrush, null, tile, radius, radius);
        }

        if (pen is not null)
        {
            context.DrawRoundedRectangle(null, pen, tile, radius, radius);
        }

        if (tile.Width >= LabelMinWidth && tile.Height >= LabelMinHeight * style.Scale)
        {
            DrawLabel(context, node, tile, style.Mono, style.Scale);
        }

        if (node.IsDirectory)
        {
            DrawFolderMarker(context, tile);
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    protected override Visual GetVisualChild(int index)
    {
        return index == 0 ? _menuHost : throw new ArgumentOutOfRangeException(nameof(index));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var point = e.GetPosition(this);
        var node = HitTest(point);

        if (node is null)
        {
            return;
        }

        _cursor = point;
        SelectedItem = node;

        if (e.ClickCount == 2 && node is { IsDirectory: true, HasChildren: true } && DrillCommand?.CanExecute(node) == true)
        {
            DrillCommand.Execute(node);
        }

        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        var node = HitTest(e.GetPosition(this));

        if (node is null)
        {
            return;
        }

        SelectedItem = node;

        if (NodeContextMenu is { } menu)
        {
            _menuHost.DataContext = node;
            menu.PlacementTarget = _menuHost;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        var node = HitTest(point);
        _cursor = point;

        if (!ReferenceEquals(node, _hover))
        {
            _hover = node;
            ShowTooltip(node);
            return;
        }

        if (node is not null && _toolTip.IsOpen)
        {
            Reposition();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = null;
        _toolTip.IsOpen = false;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        HookItems();
        FontScaleManager.Changed += OnFontScaleChanged;
        InvalidateVisual();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded)
        {
            return;
        }

        _isLoaded = false;
        UnhookItems();
        FontScaleManager.Changed -= OnFontScaleChanged;
    }

    private void OnFontScaleChanged(object? sender, double scale)
    {
        InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Resubscribe();
        InvalidateVisual();
        Dispatcher.InvokeAsync(RefreshHover, DispatcherPriority.ContextIdle);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScanNodeViewModel.IsMarkedDeleted) or nameof(ScanNodeViewModel.IsSelected) or "" or null)
        {
            InvalidateVisual();
        }
    }

    private static void OnTooltipStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((TreemapView)d)._toolTip.Style = (Style?)e.NewValue;
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (TreemapView)d;

        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= view.OnCollectionChanged;
        }

        view.ClearItemSubscriptions();

        if (view._isLoaded)
        {
            if (e.NewValue is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += view.OnCollectionChanged;
            }

            view.Resubscribe();
        }

        view.InvalidateVisual();
        view.Dispatcher.InvokeAsync(view.RefreshHover, DispatcherPriority.ContextIdle);
    }

    private static void DrawIcon(DrawingContext context, Geometry geometry, Rect target)
    {
        var bounds = geometry.Bounds;

        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var scale = Math.Min(target.Width / bounds.Width, target.Height / bounds.Height);
        var matrix = new Matrix();
        matrix.Scale(scale, scale);
        matrix.Translate(target.X + (target.Width - bounds.Width * scale) / 2 - bounds.X * scale,
            target.Y + (target.Height - bounds.Height * scale) / 2 - bounds.Y * scale);

        context.PushTransform(new MatrixTransform(matrix));
        context.DrawGeometry(LabelBrush, null, geometry);
        context.Pop();
    }

    private static Geometry ParseIcon(PackIconLucideKind kind)
    {
        var data = new PackIconLucide { Kind = kind }.Data;
        var geometry = string.IsNullOrEmpty(data) ? Geometry.Empty : Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    private static Geometry FrozenGeometry(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    private static Rect Deflate(Rect rect, double margin)
    {
        return new(rect.X + margin,
            rect.Y + margin,
            Math.Max(0, rect.Width - 2 * margin),
            Math.Max(0, rect.Height - 2 * margin));
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Brush FrozenCushion()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new(0, 0),
            EndPoint = new(0, 1),
            GradientStops =
            {
                new(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), 0),
                new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.55),
            },
        };

        brush.Freeze();
        return brush;
    }

    private static void DrawFolderMarker(DrawingContext context, Rect tile)
    {
        if (tile.Width < MarkerSize * 1.8 || tile.Height < MarkerSize * 1.8)
        {
            return;
        }

        var geometry = new StreamGeometry();

        using (var sink = geometry.Open())
        {
            sink.BeginFigure(new(tile.Right - MarkerSize, tile.Top), true, true);
            sink.LineTo(new(tile.Right, tile.Top), false, false);
            sink.LineTo(new(tile.Right, tile.Top + MarkerSize), false, false);
        }

        geometry.Freeze();
        context.DrawGeometry(MarkerBrush, null, geometry);
    }

    private static void ApplyBeak(Path beak, TooltipSide side)
    {
        switch (side)
        {
            case TooltipSide.Above:
                beak.Data = BeakDown;
                beak.HorizontalAlignment = HorizontalAlignment.Left;
                beak.VerticalAlignment = VerticalAlignment.Bottom;
                beak.Margin = new(16, 0, 0, 1);
                break;

            case TooltipSide.RightOf:
                beak.Data = BeakLeft;
                beak.HorizontalAlignment = HorizontalAlignment.Left;
                beak.VerticalAlignment = VerticalAlignment.Top;
                beak.Margin = new(1, 16, 0, 0);
                break;

            case TooltipSide.LeftOf:
                beak.Data = BeakRight;
                beak.HorizontalAlignment = HorizontalAlignment.Right;
                beak.VerticalAlignment = VerticalAlignment.Top;
                beak.Margin = new(0, 16, 1, 0);
                break;

            default:
                beak.Data = BeakUp;
                beak.HorizontalAlignment = HorizontalAlignment.Left;
                beak.VerticalAlignment = VerticalAlignment.Top;
                beak.Margin = new(16, 1, 0, 0);
                break;
        }
    }

    private T? ReadDependencyValue<T>(DependencyProperty property) where T : class
    {
        var value = GetValue(property);

        if (value is null)
        {
            return null;
        }

        return value is T typed
            ? typed
            : throw new InvalidCastException($"Значение свойства {property.Name} имеет тип {value.GetType().FullName}, ожидался {typeof(T).FullName}.");
    }

    private void ShowTooltip(ScanNodeViewModel? node)
    {
        _toolTip.IsOpen = false;

        if (node is null)
        {
            return;
        }

        if (NodeTooltipTemplate is { } template)
        {
            _toolTip.ContentTemplate = template;
            _toolTip.Content = node;
        }
        else
        {
            _toolTip.ContentTemplate = null;
            _toolTip.Content = node.Tooltip;
        }

        _toolTip.IsOpen = true;
    }

    private void RefreshHover()
    {
        if (!IsMouseOver)
        {
            return;
        }

        _cursor = Mouse.GetPosition(this);
        var node = HitTest(_cursor);

        if (ReferenceEquals(node, _hover))
        {
            Reposition();
            return;
        }

        _hover = node;
        ShowTooltip(node);
    }

    private void Reposition()
    {
        _nudge = !_nudge;
        _toolTip.HorizontalOffset = _nudge ? 0 : 0.01;
    }

    private CustomPopupPlacement[] PlaceTooltip(Size popupSize, Size targetSize, Point offset)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var bounds = new Rect(0, 0, targetSize.Width / dpi.DpiScaleX, targetSize.Height / dpi.DpiScaleY);

        if (Window.GetWindow(this) is { } window)
        {
            var origin = TranslatePoint(new(0, 0), window);
            bounds = new(-origin.X, -origin.Y, window.ActualWidth, window.ActualHeight);
        }

        var placement = TreemapTooltip.Place(_cursor, popupSize, bounds, dpi);

        _beak ??= _toolTip.Template?.FindName("Beak", _toolTip) as Path;

        if (_beak is not null)
        {
            ApplyBeak(_beak, placement.Side);
        }

        return [new(placement.DeviceTopLeft, PopupPrimaryAxis.None)];
    }

    private void HookItems()
    {
        if (ItemsSource is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += OnCollectionChanged;
        }

        Resubscribe();
    }

    private void UnhookItems()
    {
        if (ItemsSource is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged -= OnCollectionChanged;
        }

        ClearItemSubscriptions();
    }

    private void ClearItemSubscriptions()
    {
        foreach (var item in _subscribed)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        _subscribed.Clear();
    }

    private void Resubscribe()
    {
        ClearItemSubscriptions();

        if (ItemsSource is null)
        {
            return;
        }

        foreach (var item in ItemsSource)
        {
            if (item is INotifyPropertyChanged notify)
            {
                notify.PropertyChanged += OnItemPropertyChanged;
                _subscribed.Add(notify);
            }
        }
    }

    private List<ScanNodeViewModel> Nodes()
    {
        var nodes = new List<ScanNodeViewModel>();

        if (ItemsSource is null)
        {
            return nodes;
        }

        foreach (var item in ItemsSource)
        {
            if (item is ScanNodeViewModel { Weight: > 0 } node)
            {
                nodes.Add(node);
            }
        }

        return nodes;
    }

    private ScanNodeViewModel? HitTest(Point point)
    {
        foreach (var (rect, node) in _tiles)
        {
            if (rect.Contains(point))
            {
                return node;
            }
        }

        return null;
    }

    private void DrawLabel(DrawingContext context, ScanNodeViewModel node, Rect tile, FontFamily? monoFont, double scale)
    {
        var x = tile.X + Padding;
        var y = tile.Y + 4;

        var name = Text(node.Name, 12 * scale, LabelBrush, FontWeights.SemiBold, tile.Width - 2 * Padding - IconSize - IconGap, node.IsMarkedDeleted, null);
        var icon = node.IsDirectory ? FolderIcon : FileIcon;

        DrawIcon(context, icon, new(x, y + (name.Height - IconSize) / 2, IconSize, IconSize));
        context.DrawText(name, new(x + IconSize + IconGap, y));

        var size = Text(node.SizeText, 11 * scale, SubLabelBrush, FontWeights.Normal, tile.Width - 2 * Padding, false, monoFont);
        context.DrawText(size, new(x, y + name.Height + 1));
    }

    private FormattedText Text(string value, double size, Brush brush, FontWeight weight, double maxWidth, bool strikethrough, FontFamily? fontFamily)
    {
        var typeface = new Typeface(fontFamily ?? TextElement.GetFontFamily(this), FontStyles.Normal, weight, FontStretches.Normal);

        var text = new FormattedText(value,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(0, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        if (strikethrough)
        {
            text.SetTextDecorations(TextDecorations.Strikethrough);
        }

        return text;
    }

    private Pen? ResourcePen(object resourceKey, double thickness)
    {
        return TryFindResource(resourceKey) is Brush brush ? new(brush, thickness) : null;
    }

    private readonly record struct TileStyle(Pen? Selection, Pen? Deleted, FontFamily? Mono, double Intensity, double Scale);
}
