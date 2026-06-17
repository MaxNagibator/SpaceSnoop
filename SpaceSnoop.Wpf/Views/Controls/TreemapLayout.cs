using System.Diagnostics;

namespace SpaceSnoop.Wpf.Views.Controls;

public readonly record struct LayoutRect(double X, double Y, double Width, double Height);

public static class TreemapLayout
{
    public static LayoutRect[] Squarify(IReadOnlyList<double> weights, double width, double height)
    {
        var count = weights.Count;
        var result = new LayoutRect[count];

        if (count == 0 || width <= 0 || height <= 0)
        {
            return result;
        }

        double total = 0;

        for (var i = 0; i < count; i++)
        {
            total += Math.Max(0, weights[i]);
        }

        if (total <= 0)
        {
            return result;
        }

        var scale = width * height / total;
        var items = new (double Area, int Index)[count];

        for (var i = 0; i < count; i++)
        {
            items[i] = (Math.Max(0, weights[i]) * scale, i);
        }

        Array.Sort(items, static (a, b) => b.Area.CompareTo(a.Area));

        var rect = (X: 0d, Y: 0d, Width: width, Height: height);
        var rowStart = 0;
        var rowLength = 0;
        var index = 0;

        while (index < count)
        {
            var side = Math.Min(rect.Width, rect.Height);
            var area = items[index].Area;

            if (rowLength == 0 || WorstWith(items, rowStart, rowLength, area, side) <= Worst(items, rowStart, rowLength, side))
            {
                rowLength++;
                index++;
            }
            else
            {
                rect = LayoutRow(items, rowStart, rowLength, rect, result);
                rowStart = index;
                rowLength = 0;
            }
        }

        if (rowLength > 0)
        {
            LayoutRow(items, rowStart, rowLength, rect, result);
        }

        Debug.Assert(Math.Abs(Sum(result) - width * height) < 1.0, "treemap area scaling broken");

        return result;
    }

    private static (double X, double Y, double Width, double Height) LayoutRow(
        (double Area, int Index)[] items,
        int start,
        int length,
        (double X, double Y, double Width, double Height) rect,
        LayoutRect[] result)
    {
        double sum = 0;

        for (var k = 0; k < length; k++)
        {
            sum += items[start + k].Area;
        }

        if (sum <= 0)
        {
            return rect;
        }

        if (rect.Width >= rect.Height)
        {
            var thickness = sum / rect.Height;
            var y = rect.Y;

            for (var k = 0; k < length; k++)
            {
                var height = items[start + k].Area / thickness;
                result[items[start + k].Index] = new(rect.X, y, thickness, height);
                y += height;
            }

            return (rect.X + thickness, rect.Y, Math.Max(0, rect.Width - thickness), rect.Height);
        }

        var stripThickness = sum / rect.Width;
        var x = rect.X;

        for (var k = 0; k < length; k++)
        {
            var width = items[start + k].Area / stripThickness;
            result[items[start + k].Index] = new(x, rect.Y, width, stripThickness);
            x += width;
        }

        return (rect.X, rect.Y + stripThickness, rect.Width, Math.Max(0, rect.Height - stripThickness));
    }

    private static double Worst((double Area, int Index)[] items, int start, int length, double side)
    {
        double sum = 0, min = double.MaxValue, max = 0;

        for (var k = 0; k < length; k++)
        {
            var area = items[start + k].Area;
            sum += area;
            min = Math.Min(min, area);
            max = Math.Max(max, area);
        }

        return WorstCore(sum, min, max, side);
    }

    private static double WorstWith((double Area, int Index)[] items, int start, int length, double extra, double side)
    {
        double sum = extra, min = extra, max = extra;

        for (var k = 0; k < length; k++)
        {
            var area = items[start + k].Area;
            sum += area;
            min = Math.Min(min, area);
            max = Math.Max(max, area);
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

    private static double Sum(LayoutRect[] rects)
    {
        double sum = 0;

        foreach (var rect in rects)
        {
            sum += rect.Width * rect.Height;
        }

        return sum;
    }
}
