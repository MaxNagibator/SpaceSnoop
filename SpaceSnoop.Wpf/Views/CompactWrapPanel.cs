namespace SpaceSnoop.Wpf.Views;

public sealed class CompactWrapPanel : WrapPanel
{
    public static readonly DependencyProperty SingleLineProperty = DependencyProperty.Register(
        nameof(SingleLine),
        typeof(bool),
        typeof(CompactWrapPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty OptionalProperty = DependencyProperty.RegisterAttached(
        "Optional",
        typeof(bool),
        typeof(CompactWrapPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static readonly DependencyProperty KeepProperty = DependencyProperty.RegisterAttached(
        "Keep",
        typeof(bool),
        typeof(CompactWrapPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    private CompactSlot[] _slots = [];
    private double[] _widths = [];
    private double _firstHeight;
    private double _secondHeight;

    public bool SingleLine
    {
        get => (bool)GetValue(SingleLineProperty);
        set => SetValue(SingleLineProperty, value);
    }

    public static bool GetOptional(DependencyObject element)
    {
        return (bool)element.GetValue(OptionalProperty);
    }

    public static void SetOptional(DependencyObject element, bool value)
    {
        element.SetValue(OptionalProperty, value);
    }

    public static bool GetKeep(DependencyObject element)
    {
        return (bool)element.GetValue(KeepProperty);
    }

    public static void SetKeep(DependencyObject element, bool value)
    {
        element.SetValue(KeepProperty, value);
    }

    private static CompactSlot[] Plan(double available, IReadOnlyList<double> widths, IReadOnlyList<bool> optional, IReadOnlyList<bool> keep)
    {
        var slots = new CompactSlot[widths.Count];
        Array.Fill(slots, CompactSlot.First);

        var total = widths.Sum();
        total = Drop(slots, total, available, widths, i => optional[i] && !keep[i]);
        total = Drop(slots, total, available, widths, i => !keep[i]);

        var kept = 0d;

        for (var i = 1; i < widths.Count; i++)
        {
            if (keep[i])
            {
                kept += widths[i];
            }
        }

        if (total <= available || kept <= 0)
        {
            return slots;
        }

        total = 0;

        for (var i = 0; i < widths.Count; i++)
        {
            if (i > 0 && keep[i])
            {
                slots[i] = CompactSlot.Second;
            }
            else
            {
                slots[i] = CompactSlot.First;
                total += widths[i];
            }
        }

        total = Drop(slots, total, available, widths, i => optional[i] && !keep[i]);
        Drop(slots, total, available, widths, i => !keep[i]);

        return slots;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var children = InternalChildren;

        if (!SingleLine)
        {
            _slots = [];
            Reveal(children, static _ => true);
            return base.MeasureOverride(constraint);
        }

        var widths = new double[children.Count];
        var optional = new bool[children.Count];
        var keep = new bool[children.Count];

        for (var i = 0; i < children.Count; i++)
        {
            children[i].Measure(new(double.PositiveInfinity, constraint.Height));
            widths[i] = children[i].DesiredSize.Width;
            optional[i] = GetOptional(children[i]);
            keep[i] = GetKeep(children[i]);
        }

        var slots = Plan(constraint.Width, widths, optional, keep);
        _slots = slots;
        _widths = widths;
        Reveal(children, i => slots[i] != CompactSlot.None);

        if (children.Count > 0 && widths[0] > constraint.Width)
        {
            children[0].Measure(new(constraint.Width, constraint.Height));
        }

        var first = 0d;
        var second = 0d;
        _firstHeight = 0;
        _secondHeight = 0;

        for (var i = 0; i < children.Count; i++)
        {
            var height = children[i].DesiredSize.Height;

            switch (slots[i])
            {
                case CompactSlot.First:
                    first += widths[i];
                    _firstHeight = Math.Max(_firstHeight, height);
                    break;

                case CompactSlot.Second:
                    second += widths[i];
                    _secondHeight = Math.Max(_secondHeight, height);
                    break;
            }
        }

        return new(Math.Min(Math.Max(first, second), constraint.Width), _firstHeight + _secondHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;

        if (_slots.Length != children.Count)
        {
            return base.ArrangeOverride(finalSize);
        }

        var firstX = 0d;
        var secondX = 0d;
        var firstHeight = _secondHeight > 0 ? _firstHeight : finalSize.Height;
        var secondHeight = Math.Max(0, finalSize.Height - firstHeight);

        for (var i = 0; i < children.Count; i++)
        {
            switch (_slots[i])
            {
                case CompactSlot.First:
                    firstX += Place(children[i], firstX, 0, _widths[i], firstHeight, finalSize.Width);
                    break;

                case CompactSlot.Second:
                    secondX += Place(children[i], secondX, firstHeight, _widths[i], secondHeight, finalSize.Width);
                    break;

                default:
                    children[i].Arrange(default);
                    break;
            }
        }

        return finalSize;
    }

    private static double Drop(CompactSlot[] slots, double total, double available, IReadOnlyList<double> widths, Func<int, bool> candidate)
    {
        for (var i = widths.Count - 1; i > 0 && total > available; i--)
        {
            if (slots[i] == CompactSlot.First && candidate(i))
            {
                slots[i] = CompactSlot.None;
                total -= widths[i];
            }
        }

        return total;
    }

    private static double Place(UIElement child, double x, double y, double width, double height, double lineWidth)
    {
        var fitted = Math.Max(0, Math.Min(width, lineWidth - x));
        child.Arrange(new(x, y, fitted, height));

        return fitted;
    }

    private enum CompactSlot
    {
        None = 0,
        First = 1,
        Second = 2,
    }

    private static void Reveal(UIElementCollection children, Func<int, bool> shown)
    {
        for (var i = 0; i < children.Count; i++)
        {
            children[i].SetCurrentValue(IsEnabledProperty, shown(i));
        }
    }
}
