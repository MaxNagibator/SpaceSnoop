namespace SpaceSnoop.Wpf.Views;

public sealed class PriorityRowPanel : Panel
{
    private double _tailNatural;

    private static RowPlan Plan(double available, double fillMin, IReadOnlyList<double> optional, double tailMin, double tailMax)
    {
        var kept = new bool[optional.Count];
        var used = 0d;

        for (var i = 0; i < optional.Count; i++)
        {
            kept[i] = true;
            used += optional[i];
        }

        tailMax = Math.Max(tailMin, tailMax);

        for (var i = 0; i < optional.Count && (fillMin + used + tailMax) > available; i++)
        {
            kept[i] = false;
            used -= optional[i];
        }

        var tail = Math.Min(Math.Clamp(available - used - fillMin, tailMin, tailMax), Math.Max(0, available));
        var fill = Math.Max(0, available - used - tail);

        return new(fill, kept, tail);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren;

        if (children.Count < 2)
        {
            return MeasureSingle(availableSize);
        }

        var height = 0d;

        foreach (UIElement child in children)
        {
            child.Measure(new(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        _tailNatural = children[^1].DesiredSize.Width;

        if (double.IsPositiveInfinity(availableSize.Width))
        {
            var natural = 0d;

            foreach (UIElement child in children)
            {
                natural += child.DesiredSize.Width;
            }

            return new(natural - children[^1].DesiredSize.Width + TailMax(children[^1]), height);
        }

        var plan = PlanFor(availableSize.Width);
        children[0].Measure(new(plan.FillWidth, availableSize.Height));
        children[^1].Measure(new(plan.TailWidth, availableSize.Height));

        return new(availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;

        if (children.Count < 2)
        {
            foreach (UIElement child in children)
            {
                child.Arrange(new(finalSize));
            }

            return finalSize;
        }

        var plan = PlanFor(finalSize.Width);

        children[0].Arrange(new(0, 0, plan.FillWidth, finalSize.Height));

        var x = plan.FillWidth;

        for (var i = 1; i < (children.Count - 1); i++)
        {
            if (plan.Kept[i - 1])
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

        children[^1].Arrange(new(Math.Max(x, finalSize.Width - plan.TailWidth), 0, plan.TailWidth, finalSize.Height));

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
        var size = new Size();

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(availableSize);
            size = child.DesiredSize;
        }

        return size;
    }

    private RowPlan PlanFor(double available)
    {
        var children = InternalChildren;
        var optional = new double[children.Count - 2];

        for (var i = 1; i < (children.Count - 1); i++)
        {
            optional[i - 1] = children[i].DesiredSize.Width;
        }

        return Plan(available, MinOf(children[0]), optional, Math.Max(MinOf(children[^1]), _tailNatural), TailMax(children[^1]));
    }

    private sealed record RowPlan(double FillWidth, IReadOnlyList<bool> Kept, double TailWidth);
}
