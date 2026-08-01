using System.Windows.Media;

namespace SpaceSnoop.Wpf.Views.Diagnostics;

public sealed class PerformanceChartCanvas : FrameworkElement
{
    private const double Inset = 2;
    private const double DelayThickness = 1.5;
    private const double MemoryThickness = 1;

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

    protected override void OnRender(DrawingContext drawingContext)
    {
        var data = Data;
        var width = ActualWidth;
        var height = ActualHeight;

        if (data is null || !data.HasData || width <= 0 || height <= 0)
        {
            return;
        }

        DrawBands(drawingContext, data, width, height);
        DrawBaseline(drawingContext, width, height);
        DrawThreshold(drawingContext, data, width, height);
        DrawLine(drawingContext, data.Memory, MemoryBrush, MemoryThickness, [3, 3], width, height);
        DrawLine(drawingContext, data.Delay, DelayBrush, DelayThickness, null, width, height);
    }

    private void DrawBands(DrawingContext drawingContext, PerformanceChartData data, double width, double height)
    {
        if (BandBrush is not { } brush)
        {
            return;
        }

        foreach (var band in data.Bands)
        {
            drawingContext.DrawRectangle(brush, null, new(band.Start * width, 0, Math.Max(1, (band.End - band.Start) * width), height));
        }
    }

    private void DrawBaseline(DrawingContext drawingContext, double width, double height)
    {
        if (CreatePen(BaselineBrush, 1, null) is not { } pen)
        {
            return;
        }

        drawingContext.DrawLine(pen, new(0, height - 0.5), new(width, height - 0.5));
    }

    private void DrawThreshold(DrawingContext drawingContext, PerformanceChartData data, double width, double height)
    {
        if (AppDefaults.PerformanceHitchMs > data.DelayScaleMs || CreatePen(ThresholdBrush, 1, [2, 4]) is not { } pen)
        {
            return;
        }

        var y = Project(1 - (AppDefaults.PerformanceHitchMs / data.DelayScaleMs), height);

        drawingContext.DrawLine(pen, new(0, y), new(width, y));
    }

    private static void DrawLine(
        DrawingContext drawingContext,
        IReadOnlyList<Point> points,
        Brush? brush,
        double thickness,
        double[]? dashes,
        double width,
        double height)
    {
        if (points.Count < 2 || CreatePen(brush, thickness, dashes) is not { } pen)
        {
            return;
        }

        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(Project(points[0], width, height), false, false);

            for (var index = 1; index < points.Count; index++)
            {
                context.LineTo(Project(points[index], width, height), true, false);
            }
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
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

        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        return pen;
    }

    private static Point Project(Point point, double width, double height)
    {
        return new(point.X * width, Project(point.Y, height));
    }

    private static double Project(double value, double height)
    {
        return Inset + (value * Math.Max(0, height - (Inset * 2)));
    }
}
