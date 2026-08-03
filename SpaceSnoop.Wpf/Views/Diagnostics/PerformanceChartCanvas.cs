using System.Globalization;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Views.Diagnostics;

public sealed class PerformanceChartCanvas : FrameworkElement
{
    private const double Inset = 2;
    private const double DelayThickness = 1.5;
    private const double MemoryThickness = 1.2;
    private const double HeaderGap = 3;
    private const double GridThickness = 0.7;
    private const double LabelGap = 6;
    private const double PlotGap = 4;
    private const double TrackHeight = 12;
    private const double DelayShare = 0.62;
    private const double BaseFontSize = 11;
    private const double DelayDotRadius = 2;
    private const double MemoryDotRadius = 1.4;
    private const double HitchRadius = 3.5;
    private const double TooltipPadding = 8;
    private const double TooltipGap = 10;
    private const double TooltipRadius = 4;
    private const double TimeTicksMinWidth = 260;
    private const double PlotMinHeight = 24;

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data),
            typeof(PerformanceChartData),
            typeof(PerformanceChartCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DelayBrushProperty =
        DependencyProperty.Register(nameof(DelayBrush),
            typeof(Brush),
            typeof(PerformanceChartCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MemoryBrushProperty =
        DependencyProperty.Register(nameof(MemoryBrush),
            typeof(Brush),
            typeof(PerformanceChartCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BandBrushProperty =
        DependencyProperty.Register(nameof(BandBrush),
            typeof(Brush),
            typeof(PerformanceChartCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThresholdBrushProperty =
        DependencyProperty.Register(nameof(ThresholdBrush),
            typeof(Brush),
            typeof(PerformanceChartCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BaselineBrushProperty =
        DependencyProperty.Register(nameof(BaselineBrush),
            typeof(Brush),
            typeof(PerformanceChartCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty =
        DependencyProperty.Register(nameof(LabelBrush),
            typeof(Brush),
            typeof(PerformanceChartCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SurfaceBrushProperty =
        DependencyProperty.Register(nameof(SurfaceBrush),
            typeof(Brush),
            typeof(PerformanceChartCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextBrushProperty =
        DependencyProperty.Register(nameof(TextBrush),
            typeof(Brush),
            typeof(PerformanceChartCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private Rect _delayPlot;
    private Rect _memoryPlot;
    private int _cursorIndex = -1;
    private bool _isLoaded;

    public PerformanceChartCanvas()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public PerformanceChartData? Data
    {
        get => (PerformanceChartData?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Brush? DelayBrush
    {
        get => (Brush?)GetValue(DelayBrushProperty);
        set => SetValue(DelayBrushProperty, value);
    }

    public Brush? MemoryBrush
    {
        get => (Brush?)GetValue(MemoryBrushProperty);
        set => SetValue(MemoryBrushProperty, value);
    }

    public Brush? BandBrush
    {
        get => (Brush?)GetValue(BandBrushProperty);
        set => SetValue(BandBrushProperty, value);
    }

    public Brush? ThresholdBrush
    {
        get => (Brush?)GetValue(ThresholdBrushProperty);
        set => SetValue(ThresholdBrushProperty, value);
    }

    public Brush? BaselineBrush
    {
        get => (Brush?)GetValue(BaselineBrushProperty);
        set => SetValue(BaselineBrushProperty, value);
    }

    public Brush? LabelBrush
    {
        get => (Brush?)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public Brush? SurfaceBrush
    {
        get => (Brush?)GetValue(SurfaceBrushProperty);
        set => SetValue(SurfaceBrushProperty, value);
    }

    public Brush? TextBrush
    {
        get => (Brush?)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var data = Data;
        var width = ActualWidth;
        var height = ActualHeight;

        _delayPlot = Rect.Empty;
        _memoryPlot = Rect.Empty;

        if (data is null || !data.HasData || width <= 0 || height <= 0)
        {
            return;
        }

        drawingContext.DrawRectangle(Brushes.Transparent, null, new(0, 0, width, height));

        var fontSize = BaseFontSize * FontScaleManager.Current;
        var lineHeight = Math.Ceiling(fontSize * 1.45);
        var labelWidth = LabelColumnWidth(data, fontSize);
        var left = Inset + labelWidth + LabelGap;
        var right = width - Inset;

        if (right - left < 40)
        {
            return;
        }

        var headerHeight = lineHeight + HeaderGap;
        var trackHeight = data.Bands.Count > 0 ? TrackHeight + PlotGap : 0;
        var body = height - (Inset * 2) - lineHeight - trackHeight - (headerHeight * 2) - PlotGap;

        if (body < PlotMinHeight * 2)
        {
            return;
        }

        var top = Inset;

        DrawText(drawingContext, "Отклик, мс", fontSize, LabelBrush, new(left, top));
        top += headerHeight;

        var delayHeight = Math.Round(body * DelayShare);
        _delayPlot = new(left, top, right - left, delayHeight);
        top += delayHeight + PlotGap;

        DrawText(drawingContext, "Память, управляемая", fontSize, LabelBrush, new(left, top));
        top += headerHeight;

        _memoryPlot = new(left, top, right - left, body - delayHeight);
        top += _memoryPlot.Height;

        DrawGrid(drawingContext, _delayPlot, data.DelayScale, fontSize, lineHeight);
        DrawGrid(drawingContext, _memoryPlot, data.MemoryScale, fontSize, lineHeight);
        DrawThreshold(drawingContext, data, fontSize);
        DrawSeries(drawingContext, data.Memory, _memoryPlot, MemoryBrush, MemoryThickness, null, data.ShowDots ? MemoryDotRadius : 0);
        DrawSeries(drawingContext, data.Delay, _delayPlot, DelayBrush, DelayThickness, null, data.ShowDots ? DelayDotRadius : 0);
        DrawHitches(drawingContext, data);
        DrawTimeAxis(drawingContext, data, top, fontSize, lineHeight);
        DrawTrack(drawingContext, data, top + lineHeight + PlotGap, fontSize);
        DrawCursor(drawingContext, data, fontSize, lineHeight);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Track(NearestIndex(e.GetPosition(this)));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        Track(-1);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        FontScaleManager.Changed += OnFontScaleChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded)
        {
            return;
        }

        _isLoaded = false;
        FontScaleManager.Changed -= OnFontScaleChanged;
    }

    private void OnFontScaleChanged(object? sender, double scale)
    {
        InvalidateVisual();
    }

    private void Track(int index)
    {
        if (index == _cursorIndex)
        {
            return;
        }

        _cursorIndex = index;
        InvalidateVisual();
    }

    internal int NearestIndex(Point position)
    {
        var data = Data;

        if (data is null || !data.HasData || _delayPlot.IsEmpty || _delayPlot.Width <= 0)
        {
            return -1;
        }

        if (position.X < _delayPlot.Left - LabelGap || position.X > _delayPlot.Right + LabelGap)
        {
            return -1;
        }

        if (position.Y < _delayPlot.Top || position.Y > _memoryPlot.Bottom)
        {
            return -1;
        }

        var offset = (position.X - _delayPlot.Left) / _delayPlot.Width;
        var best = 0;

        for (var index = 1; index < data.Points.Count; index++)
        {
            if (Math.Abs(data.Points[index].Offset - offset) < Math.Abs(data.Points[best].Offset - offset))
            {
                best = index;
            }
        }

        return best;
    }

    private void DrawGrid(DrawingContext drawingContext, Rect plot, PerformanceScale scale, double fontSize, double lineHeight)
    {
        if (CreatePen(BaselineBrush, GridThickness, null) is not { } pen)
        {
            return;
        }

        foreach (var tick in Fit(scale.Ticks, plot.Height, lineHeight))
        {
            var y = Math.Round(plot.Top + (tick.Offset * plot.Height)) + 0.5;

            drawingContext.DrawLine(pen, new(plot.Left, y), new(plot.Right, y));

            var text = Text(tick.Label, fontSize, LabelBrush);

            if (text is not null)
            {
                drawingContext.DrawText(text, new(plot.Left - LabelGap - text.Width, y - (text.Height / 2)));
            }
        }
    }

    private void DrawThreshold(DrawingContext drawingContext, PerformanceChartData data, double fontSize)
    {
        if (AppDefaults.PerformanceHitchMs > data.DelayScale.Max || CreatePen(ThresholdBrush, 1, [2, 4]) is not { } pen)
        {
            return;
        }

        var y = _delayPlot.Top + (data.DelayScale.Offset(AppDefaults.PerformanceHitchMs) * _delayPlot.Height);

        drawingContext.DrawLine(pen, new(_delayPlot.Left, y), new(_delayPlot.Right, y));

        var text = Text($"порог {AppDefaults.PerformanceHitchMs} мс", fontSize, ThresholdBrush);

        if (text is not null && text.Width + LabelGap < _delayPlot.Width)
        {
            drawingContext.DrawText(text, new(_delayPlot.Right - text.Width, y - text.Height - 1));
        }
    }

    private void DrawSeries(
        DrawingContext drawingContext,
        IReadOnlyList<IReadOnlyList<Point>> series,
        Rect plot,
        Brush? brush,
        double thickness,
        double[]? dashes,
        double dotRadius)
    {
        if (CreatePen(brush, thickness, dashes) is not { } pen)
        {
            return;
        }

        foreach (var segment in series)
        {
            if (segment.Count > 1)
            {
                drawingContext.DrawGeometry(null, pen, Line(segment, plot));
            }

            var dot = segment.Count == 1 ? Math.Max(dotRadius, DelayDotRadius) : dotRadius;

            if (dot <= 0)
            {
                continue;
            }

            foreach (var point in segment)
            {
                drawingContext.DrawEllipse(brush, null, Project(point, plot), dot, dot);
            }
        }
    }

    private void DrawHitches(DrawingContext drawingContext, PerformanceChartData data)
    {
        if (CreatePen(ThresholdBrush, 2, null) is not { } pen)
        {
            return;
        }

        foreach (var hitch in data.Hitches)
        {
            drawingContext.DrawEllipse(SurfaceBrush, pen, Project(new(hitch.X, hitch.Y), _delayPlot), HitchRadius, HitchRadius);
        }
    }

    private void DrawTimeAxis(DrawingContext drawingContext, PerformanceChartData data, double top, double fontSize, double lineHeight)
    {
        var ticks = data.TimeTicks;

        if (ticks.Count > 2 && _delayPlot.Width < TimeTicksMinWidth)
        {
            ticks = [ticks[0], ticks[^1]];
        }

        foreach (var tick in ticks)
        {
            var text = Text(tick.Label, fontSize, LabelBrush);

            if (text is null)
            {
                continue;
            }

            var x = _delayPlot.Left + (tick.Offset * _delayPlot.Width) - (text.Width * tick.Offset);

            drawingContext.DrawText(text, new(x, top + ((lineHeight - text.Height) / 2)));
        }
    }

    private void DrawTrack(DrawingContext drawingContext, PerformanceChartData data, double top, double fontSize)
    {
        if (data.Bands.Count == 0 || BandBrush is not { } brush)
        {
            return;
        }

        foreach (var band in data.Bands)
        {
            var x = _delayPlot.Left + (band.Start * _delayPlot.Width);
            var width = Math.Max(1, (band.End - band.Start) * _delayPlot.Width);
            var rect = new Rect(x, top, width, TrackHeight);

            drawingContext.DrawRoundedRectangle(brush, null, rect, 2, 2);

            var text = Text(band.Name, fontSize * 0.9, LabelBrush);

            if (text is null || text.Width + 6 > width)
            {
                continue;
            }

            drawingContext.DrawText(text, new(x + 4, top + ((TrackHeight - text.Height) / 2)));
        }
    }

    private void DrawCursor(DrawingContext drawingContext, PerformanceChartData data, double fontSize, double lineHeight)
    {
        if (_cursorIndex < 0 || _cursorIndex >= data.Points.Count || CreatePen(LabelBrush, 1, [2, 3]) is not { } pen)
        {
            return;
        }

        var point = data.Points[_cursorIndex];
        var x = Math.Round(_delayPlot.Left + (point.Offset * _delayPlot.Width)) + 0.5;

        drawingContext.DrawLine(pen, new(x, _delayPlot.Top), new(x, _memoryPlot.Bottom));

        var delay = new Point(x, _delayPlot.Top + (data.DelayScale.Offset(point.UiDelayMs) * _delayPlot.Height));
        var memory = new Point(x, _memoryPlot.Top + (data.MemoryScale.Offset(point.ManagedBytes) * _memoryPlot.Height));

        drawingContext.DrawEllipse(DelayBrush, null, delay, DelayDotRadius + 1, DelayDotRadius + 1);
        drawingContext.DrawEllipse(MemoryBrush, null, memory, DelayDotRadius, DelayDotRadius);

        DrawTooltip(drawingContext, point, x, fontSize, lineHeight);
    }

    private void DrawTooltip(DrawingContext drawingContext, PerformanceCursorPoint point, double cursorX, double fontSize, double lineHeight)
    {
        var lines = new List<FormattedText>(4);

        if (Text(PerformanceFormat.Age(point.AgeMs / 1000), fontSize, LabelBrush) is { } age)
        {
            lines.Add(age);
        }

        if (Text($"отклик {Math.Round(point.UiDelayMs):N0} мс", fontSize, TextBrush) is { } delay)
        {
            lines.Add(delay);
        }

        if (Text($"память {SizeFormatter.Format(point.ManagedBytes)}", fontSize, TextBrush) is { } memory)
        {
            lines.Add(memory);
        }

        if (!string.IsNullOrEmpty(point.Operation) && Text(point.Operation, fontSize, LabelBrush) is { } operation)
        {
            lines.Add(operation);
        }

        if (lines.Count == 0)
        {
            return;
        }

        var contentWidth = lines.Max(static line => line.Width);
        var width = contentWidth + (TooltipPadding * 2);
        var height = (lines.Count * lineHeight) + TooltipPadding;
        var x = cursorX + TooltipGap;

        if (x + width > ActualWidth - Inset)
        {
            x = cursorX - TooltipGap - width;
        }

        x = Math.Max(Inset, x);

        var y = Math.Max(Inset, Math.Min(_delayPlot.Top, _memoryPlot.Bottom - height));

        drawingContext.DrawRoundedRectangle(SurfaceBrush,
            CreatePen(BaselineBrush, 1, null),
            new(x, y, width, height),
            TooltipRadius,
            TooltipRadius);

        var line = y + (TooltipPadding / 2);

        foreach (var text in lines)
        {
            drawingContext.DrawText(text, new(x + TooltipPadding, line));
            line += lineHeight;
        }
    }

    private double LabelColumnWidth(PerformanceChartData data, double fontSize)
    {
        var width = 0d;

        foreach (var tick in data.DelayScale.Ticks.Concat(data.MemoryScale.Ticks))
        {
            width = Math.Max(width, Text(tick.Label, fontSize, LabelBrush)?.Width ?? 0);
        }

        return width;
    }

    private void DrawText(DrawingContext drawingContext, string value, double fontSize, Brush? brush, Point origin)
    {
        if (Text(value, fontSize, brush) is { } text)
        {
            drawingContext.DrawText(text, origin);
        }
    }

    private FormattedText? Text(string value, double fontSize, Brush? brush)
    {
        if (brush is null || string.IsNullOrEmpty(value))
        {
            return null;
        }

        var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        return new(value,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
    }

    private static IReadOnlyList<PerformanceTick> Fit(IReadOnlyList<PerformanceTick> ticks, double height, double lineHeight)
    {
        return ticks.Count <= 2 || height >= ticks.Count * lineHeight * 1.4 ? ticks : [ticks[0], ticks[^1]];
    }

    private static Geometry Line(IReadOnlyList<Point> points, Rect plot)
    {
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(Project(points[0], plot), false, false);

            for (var index = 1; index < points.Count; index++)
            {
                context.LineTo(Project(points[index], plot), true, false);
            }
        }

        geometry.Freeze();

        return geometry;
    }

    private static Pen? CreatePen(Brush? brush, double thickness, double[]? dashes)
    {
        if (brush is null)
        {
            return null;
        }

        var pen = new Pen(brush, thickness);

        if (dashes is not null)
        {
            pen.DashStyle = new(new DoubleCollection(dashes), 0);
            pen.DashCap = PenLineCap.Flat;
        }

        return pen;
    }

    private static Point Project(Point point, Rect plot)
    {
        return new(plot.Left + (point.X * plot.Width), plot.Top + (point.Y * plot.Height));
    }
}
