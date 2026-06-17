using System.Diagnostics;

namespace SpaceSnoop.Wpf.Views.Controls;

public sealed class TreemapPanel : Panel
{
    public static readonly DependencyProperty WeightProperty =
        DependencyProperty.RegisterAttached("Weight",
            typeof(double),
            typeof(TreemapPanel),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static void SetWeight(DependencyObject element, double value)
    {
        element.SetValue(WeightProperty, value);
    }

    public static double GetWeight(DependencyObject element)
    {
        return (double)element.GetValue(WeightProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(availableSize);
        }

        return new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var bounds = new Rect(0, 0, finalSize.Width, finalSize.Height);
        var tiles = new List<Tile>(InternalChildren.Count);
        double totalWeight = 0;

        foreach (UIElement child in InternalChildren)
        {
            var weight = GetWeight(child);

            if (weight > 0 && bounds.Width > 0 && bounds.Height > 0)
            {
                tiles.Add(new(child, weight));
                totalWeight += weight;
            }
            else
            {
                child.Arrange(default);
            }
        }

        if (tiles.Count == 0 || totalWeight <= 0)
        {
            return finalSize;
        }

        var scale = bounds.Width * bounds.Height / totalWeight;

        for (var i = 0; i < tiles.Count; i++)
        {
            tiles[i] = tiles[i] with { Area = tiles[i].Weight * scale };
        }

        tiles.Sort(static (a, b) => b.Area.CompareTo(a.Area));

        Debug.Assert(Math.Abs(tiles.Sum(t => t.Area) - bounds.Width * bounds.Height) < 1.0, "treemap area scaling broken");

        Squarify(tiles, bounds);

        return finalSize;
    }

    private static void Squarify(List<Tile> tiles, Rect bounds)
    {
        var rect = bounds;
        var row = new List<Tile>();
        var index = 0;

        while (index < tiles.Count)
        {
            var side = Math.Min(rect.Width, rect.Height);
            var candidate = tiles[index];

            if (row.Count == 0 || WorstWith(row, candidate, side) <= Worst(row, side))
            {
                row.Add(candidate);
                index++;
            }
            else
            {
                rect = LayoutRow(row, rect);
                row.Clear();
            }
        }

        if (row.Count > 0)
        {
            LayoutRow(row, rect);
        }
    }

    private static Rect LayoutRow(List<Tile> row, Rect rect)
    {
        double sum = 0;

        foreach (var tile in row)
        {
            sum += tile.Area;
        }

        if (sum <= 0)
        {
            return rect;
        }

        if (rect.Width >= rect.Height)
        {
            var thickness = sum / rect.Height;
            var y = rect.Y;

            foreach (var tile in row)
            {
                var height = tile.Area / thickness;
                tile.Element.Arrange(new(rect.X, y, thickness, height));
                y += height;
            }

            return new(rect.X + thickness, rect.Y, Math.Max(0, rect.Width - thickness), rect.Height);
        }

        var stripThickness = sum / rect.Width;
        var x = rect.X;

        foreach (var tile in row)
        {
            var width = tile.Area / stripThickness;
            tile.Element.Arrange(new(x, rect.Y, width, stripThickness));
            x += width;
        }

        return new(rect.X, rect.Y + stripThickness, rect.Width, Math.Max(0, rect.Height - stripThickness));
    }

    private static double Worst(List<Tile> row, double side)
    {
        double sum = 0, min = double.MaxValue, max = 0;

        foreach (var tile in row)
        {
            sum += tile.Area;
            min = Math.Min(min, tile.Area);
            max = Math.Max(max, tile.Area);
        }

        return WorstCore(sum, min, max, side);
    }

    private static double WorstWith(List<Tile> row, Tile extra, double side)
    {
        double sum = extra.Area, min = extra.Area, max = extra.Area;

        foreach (var tile in row)
        {
            sum += tile.Area;
            min = Math.Min(min, tile.Area);
            max = Math.Max(max, tile.Area);
        }

        return WorstCore(sum, min, max, side);
    }

    private static double WorstCore(double sum, double min, double max, double side)
    {
        if (sum <= 0 || side <= 0 || min <= 0)
        {
            return double.MaxValue;
        }

        var side2 = side * side;
        var sum2 = sum * sum;

        return Math.Max(side2 * max / sum2, sum2 / (side2 * min));
    }

    private readonly record struct Tile(UIElement Element, double Weight)
    {
        public double Area { get; init; }
    }
}
