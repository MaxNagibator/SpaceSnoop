using MahApps.Metro.IconPacks;
using SpaceSnoop.Wpf.Converters;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Views.Controls;

public sealed class TreemapView : FrameworkElement
{
    private const double LabelMinWidth = 46;
    private const double LabelMinHeight = 32;
    private const double Gap = 1.5;
    private const double CornerRadius = 3;
    private const double Padding = 5;
    private const double IconSize = 12;
    private const double IconGap = 5;

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

    public static readonly DependencyProperty NodeContextMenuProperty =
        DependencyProperty.Register(nameof(NodeContextMenu), typeof(ContextMenu), typeof(TreemapView));

    private static readonly SolidColorBrush LabelBrush = Frozen(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly SolidColorBrush SubLabelBrush = Frozen(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly Geometry FolderIcon = ParseIcon(PackIconLucideKind.Folder);
    private static readonly Geometry FileIcon = ParseIcon(PackIconLucideKind.File);

    private readonly FrameworkElement _menuHost = new();
    private readonly List<INotifyPropertyChanged> _subscribed = [];

    private (Rect Rect, ScanNodeViewModel Node)[] _tiles = [];
    private ScanNodeViewModel? _hover;

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
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
        get => (ICommand?)GetValue(DrillCommandProperty);
        set => SetValue(DrillCommandProperty, value);
    }

    public ContextMenu? NodeContextMenu
    {
        get => (ContextMenu?)GetValue(NodeContextMenuProperty);
        set => SetValue(NodeContextMenuProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        var width = ActualWidth;
        var height = ActualHeight;

        context.DrawRectangle(Brushes.Transparent, null, new(0, 0, width, height));

        var nodes = Nodes();
        var weights = new double[nodes.Count];

        for (var i = 0; i < nodes.Count; i++)
        {
            weights[i] = nodes[i].Weight;
        }

        var layout = TreemapLayout.Squarify(weights, width, height);
        var tiles = new (Rect, ScanNodeViewModel)[nodes.Count];

        var selectionPen = ResourcePen("Fg.Primary", 1.5);
        var deletedPen = ResourcePen("State.Error", 1.5);
        var monoFont = TryFindResource("Font.Mono") as FontFamily;
        var intensity = Intensity;

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var bounds = new Rect(layout[i].X, layout[i].Y, layout[i].Width, layout[i].Height);
            tiles[i] = (bounds, node);

            var tile = Deflate(bounds, Gap);

            if (tile.Width <= 0 || tile.Height <= 0)
            {
                continue;
            }

            var fill = new SolidColorBrush(HeatColor.From(node.Fraction, intensity));

            if (node.IsMarkedDeleted)
            {
                fill.Opacity = 0.5;
            }

            fill.Freeze();

            var pen = ReferenceEquals(node, SelectedItem) ? selectionPen
                : node.IsMarkedDeleted ? deletedPen
                : null;

            context.DrawRoundedRectangle(fill, pen, tile, CornerRadius, CornerRadius);

            if (tile.Width >= LabelMinWidth && tile.Height >= LabelMinHeight)
            {
                DrawLabel(context, node, tile, monoFont);
            }
        }

        _tiles = tiles;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var node = HitTest(e.GetPosition(this));

        if (node is null)
        {
            return;
        }

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
        var node = HitTest(e.GetPosition(this));

        if (!ReferenceEquals(node, _hover))
        {
            _hover = node;
            ToolTip = node?.Tooltip;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = null;
        ToolTip = null;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Resubscribe();
        InvalidateVisual();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScanNodeViewModel.IsMarkedDeleted) or nameof(ScanNodeViewModel.IsSelected) or "" or null)
        {
            InvalidateVisual();
        }
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (TreemapView)d;

        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= view.OnCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += view.OnCollectionChanged;
        }

        view.Resubscribe();
        view.InvalidateVisual();
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

    private void Resubscribe()
    {
        foreach (var item in _subscribed)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        _subscribed.Clear();

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

    private void DrawLabel(DrawingContext context, ScanNodeViewModel node, Rect tile, FontFamily? monoFont)
    {
        var x = tile.X + Padding;
        var y = tile.Y + 4;

        var name = Text(node.Name, 12, LabelBrush, FontWeights.SemiBold, tile.Width - 2 * Padding - IconSize - IconGap, node.IsMarkedDeleted, null);
        var icon = node.IsDirectory ? FolderIcon : FileIcon;

        DrawIcon(context, icon, new(x, y + (name.Height - IconSize) / 2, IconSize, IconSize));
        context.DrawText(name, new(x + IconSize + IconGap, y));

        var size = Text(node.SizeText, 11, SubLabelBrush, FontWeights.Normal, tile.Width - 2 * Padding, false, monoFont);
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
}
