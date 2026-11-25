namespace SpaceSnoop.Wpf.Views;

public sealed class PriorityRowPanel : Panel
{
    private const int InlineOptionalLimit = 64;

    private double _tailNatural;
    private bool _hasCachedPlan;
    private double _cachedAvailable;
    private int _cachedCount;
    private RowPlan _cachedPlan;

    protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
    {
        _hasCachedPlan = false;
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _hasCachedPlan = false;

        var children = InternalChildren;
        var count = children.Count;

        if (count < 2)
        {
            return MeasureSingle(availableSize);
        }

        var height = 0d;

        for (var i = 0; i < count; i++)
        {
            var child = children[i];
            child.Measure(new(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        _tailNatural = children[count - 1].DesiredSize.Width;

        if (double.IsPositiveInfinity(availableSize.Width))
        {
            var natural = 0d;

            for (var i = 0; i < count; i++)
            {
                natural += children[i].DesiredSize.Width;
            }

            return new(natural - _tailNatural + TailMax(children[count - 1]), height);
        }

        var plan = PlanFor(availableSize.Width);
        _cachedAvailable = availableSize.Width;
        _cachedCount = count;
        _cachedPlan = plan;
        _hasCachedPlan = true;

        children[0].Measure(new(plan.FillWidth, availableSize.Height));
        children[count - 1].Measure(new(plan.TailWidth, availableSize.Height));

        return new(availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;
        var count = children.Count;

        if (count < 2)
        {
            for (var i = 0; i < count; i++)
            {
                children[i].Arrange(new(finalSize));
            }

            return finalSize;
        }

        var plan = _hasCachedPlan
            && _cachedCount == count
            && BitConverter.DoubleToInt64Bits(_cachedAvailable) == BitConverter.DoubleToInt64Bits(finalSize.Width)
            ? _cachedPlan
            : PlanFor(finalSize.Width);

        children[0].Arrange(new(0, 0, plan.FillWidth, finalSize.Height));

        var x = plan.FillWidth;

        for (var i = 1; i < (count - 1); i++)
        {
            if (plan.IsKept(i - 1))
            {
                var width = children[i].DesiredSize.Width;
                children[i].Arrange(new(x, 0, width, finalSize.Height));
                x += width;
            }
            else
            {
                children[i].Arrange(new(0, 0, 0, 0));
            }
        }

        children[count - 1].Arrange(new(Math.Max(x, finalSize.Width - plan.TailWidth), 0, plan.TailWidth, finalSize.Height));

        return finalSize;
    }

    private static double MinOf(UIElement element)
    {
        return element is FrameworkElement framework ? framework.MinWidth : 0;
    }

    private static double TailMax(UIElement element)
    {
        return element is FrameworkElement { MaxWidth: < double.PositiveInfinity } framework
            ? framework.MaxWidth
            : element.DesiredSize.Width;
    }

    private Size MeasureSingle(Size availableSize)
    {
        var children = InternalChildren;
        var count = children.Count;
        var size = new Size();

        for (var i = 0; i < count; i++)
        {
            var child = children[i];
            child.Measure(availableSize);
            size = child.DesiredSize;
        }

        return size;
    }

    private RowPlan PlanFor(double available)
    {
        var children = InternalChildren;
        var count = children.Count;
        var optionalCount = count - 2;
        var fillMin = MinOf(children[0]);
        var tail = children[count - 1];
        var tailMin = Math.Max(MinOf(tail), _tailNatural);
        var tailMax = Math.Max(tailMin, TailMax(tail));

        var used = 0d;

        for (var i = 1; i < (count - 1); i++)
        {
            used += children[i].DesiredSize.Width;
        }

        if (optionalCount <= InlineOptionalLimit)
        {
            var keptMask = optionalCount switch
            {
                0 => 0UL,
                InlineOptionalLimit => ulong.MaxValue,
                _ => (1UL << optionalCount) - 1,
            };

            for (var i = 0; i < optionalCount && (fillMin + used + tailMax) > available; i++)
            {
                keptMask &= ~(1UL << i);
                used -= children[i + 1].DesiredSize.Width;
            }

            var tailWidth = Math.Min(Math.Clamp(available - used - fillMin, tailMin, tailMax), Math.Max(0, available));
            var fillWidth = Math.Max(0, available - used - tailWidth);

            return new(fillWidth, keptMask, null, tailWidth);
        }

        var overflowKept = new bool[optionalCount];
        Array.Fill(overflowKept, true);

        for (var i = 0; i < optionalCount && (fillMin + used + tailMax) > available; i++)
        {
            overflowKept[i] = false;
            used -= children[i + 1].DesiredSize.Width;
        }

        var overflowTail = Math.Min(Math.Clamp(available - used - fillMin, tailMin, tailMax), Math.Max(0, available));
        var overflowFill = Math.Max(0, available - used - overflowTail);

        return new(overflowFill, 0, overflowKept, overflowTail);
    }

    private readonly record struct RowPlan(double FillWidth, ulong KeptMask, bool[]? OverflowKept, double TailWidth)
    {
        public bool IsKept(int index)
        {
            return OverflowKept is not null
                ? OverflowKept[index]
                : (KeptMask & (1UL << index)) != 0;
        }
    }
}
