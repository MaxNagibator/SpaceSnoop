namespace SpaceSnoop.Wpf.Views;

public sealed class StretchWrapPanel : Panel
{
    public static readonly DependencyProperty StretchProperty = DependencyProperty.RegisterAttached("Stretch",
        typeof(bool),
        typeof(StretchWrapPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(nameof(ItemWidth),
        typeof(double),
        typeof(StretchWrapPanel),
        new FrameworkPropertyMetadata(340d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty MaxItemWidthProperty = DependencyProperty.Register(nameof(MaxItemWidth),
        typeof(double),
        typeof(StretchWrapPanel),
        new FrameworkPropertyMetadata(double.PositiveInfinity, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty StretchWidthProperty = DependencyProperty.RegisterAttached("StretchWidth",
        typeof(double),
        typeof(StretchWrapPanel),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty StandaloneProperty = DependencyProperty.RegisterAttached("Standalone",
        typeof(bool),
        typeof(StretchWrapPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double MaxItemWidth
    {
        get => (double)GetValue(MaxItemWidthProperty);
        set => SetValue(MaxItemWidthProperty, value);
    }

    public static void SetStretch(UIElement element, bool value)
    {
        element.SetValue(StretchProperty, value);
    }

    public static bool GetStretch(UIElement element)
    {
        return (bool)element.GetValue(StretchProperty);
    }

    public static void SetStretchWidth(UIElement element, double value)
    {
        element.SetValue(StretchWidthProperty, value);
    }

    public static double GetStretchWidth(UIElement element)
    {
        return (double)element.GetValue(StretchWidthProperty);
    }

    public static void SetStandalone(UIElement element, bool value)
    {
        element.SetValue(StandaloneProperty, value);
    }

    public static bool GetStandalone(UIElement element)
    {
        return (bool)element.GetValue(StandaloneProperty);
    }

    public static List<(int Start, int Count)> PackRows(IReadOnlyList<double> widths, double lineWidth, IReadOnlyList<bool>? standalone = null)
    {
        var rows = new List<(int, int)>();
        var start = 0;
        var rowWidth = 0d;

        for (var i = 0; i < widths.Count; i++)
        {
            var alone = standalone is not null && standalone[i];
            var overflow = rowWidth + widths[i] > lineWidth;
            var groupOverflow = alone && i + 1 < widths.Count && rowWidth + widths[i] + widths[i + 1] > lineWidth;

            if ((overflow || groupOverflow) && i > start)
            {
                rows.Add((start, i - start));
                start = i;
                rowWidth = 0;
            }

            rowWidth += widths[i];

            if (alone && (overflow || groupOverflow) && i + 1 < widths.Count)
            {
                rows.Add((start, i - start + 1));
                start = i + 1;
                rowWidth = 0;
            }
        }

        if (widths.Count > start)
        {
            rows.Add((start, widths.Count - start));
        }

        return rows;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var line = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width;
        var widths = new double[InternalChildren.Count];
        var heights = new double[InternalChildren.Count];

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            var stretch = GetStretch(child);
            var baseWidth = StretchBase(child);
            child.Measure(new(stretch ? baseWidth : double.PositiveInfinity, double.PositiveInfinity));
            widths[i] = stretch ? baseWidth : child.DesiredSize.Width;
            heights[i] = child.DesiredSize.Height;
        }

        var totalWidth = 0d;
        var totalHeight = 0d;

        foreach (var (start, count) in PackRows(widths, line, StandaloneFlags()))
        {
            var rowWidth = 0d;
            var rowHeight = 0d;

            for (var i = start; i < start + count; i++)
            {
                rowWidth += widths[i];
                rowHeight = Math.Max(rowHeight, heights[i]);
            }

            totalWidth = Math.Max(totalWidth, rowWidth);
            totalHeight += rowHeight;
        }

        return new(double.IsInfinity(line) ? totalWidth : Math.Min(totalWidth, line), totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var widths = new double[InternalChildren.Count];

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            widths[i] = GetStretch(InternalChildren[i]) ? StretchBase(InternalChildren[i]) : InternalChildren[i].DesiredSize.Width;
        }

        var y = 0d;

        foreach (var (start, count) in PackRows(widths, finalSize.Width, StandaloneFlags()))
        {
            var fixedWidth = 0d;
            var rowHeight = 0d;
            var stretchCount = 0;

            for (var i = start; i < start + count; i++)
            {
                var child = InternalChildren[i];
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);

                if (GetStretch(child))
                {
                    fixedWidth += StretchBase(child);
                    stretchCount++;
                }
                else
                {
                    fixedWidth += child.DesiredSize.Width;
                }
            }

            var extra = stretchCount > 0 ? Math.Max(0, finalSize.Width - fixedWidth) / stretchCount : 0;
            var x = count == 1 && GetStandalone(InternalChildren[start])
                ? Math.Max(0, finalSize.Width - fixedWidth) / 2
                : 0d;

            for (var i = start; i < start + count; i++)
            {
                var child = InternalChildren[i];
                var width = GetStretch(child) ? Math.Min(MaxItemWidth, StretchBase(child) + extra) : child.DesiredSize.Width;
                child.Arrange(new(x, y, width, rowHeight));
                x += width;
            }

            y += rowHeight;
        }

        return finalSize;
    }

    private bool[] StandaloneFlags()
    {
        var flags = new bool[InternalChildren.Count];

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            flags[i] = GetStandalone(InternalChildren[i]);
        }

        return flags;
    }

    private double StretchBase(UIElement child)
    {
        var width = GetStretchWidth(child);
        return double.IsNaN(width) ? ItemWidth : width;
    }
}
