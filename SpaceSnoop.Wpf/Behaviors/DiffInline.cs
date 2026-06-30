using SpaceSnoop.Wpf.Diff;
using System.Windows.Documents;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Behaviors;

public static class DiffInline
{
    public static readonly DependencyProperty SpansProperty = DependencyProperty.RegisterAttached("Spans", typeof(IReadOnlyList<DiffSpan>), typeof(DiffInline), new(null, OnChanged));

    public static readonly DependencyProperty ChangedBrushProperty = DependencyProperty.RegisterAttached("ChangedBrush", typeof(Brush), typeof(DiffInline), new(null, OnChanged));

    public static IReadOnlyList<DiffSpan>? GetSpans(DependencyObject element)
    {
        return (IReadOnlyList<DiffSpan>?)element.GetValue(SpansProperty);
    }

    public static void SetSpans(DependencyObject element, IReadOnlyList<DiffSpan>? value)
    {
        element.SetValue(SpansProperty, value);
    }

    public static Brush? GetChangedBrush(DependencyObject element)
    {
        return (Brush?)element.GetValue(ChangedBrushProperty);
    }

    public static void SetChangedBrush(DependencyObject element, Brush? value)
    {
        element.SetValue(ChangedBrushProperty, value);
    }

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock block)
        {
            return;
        }

        block.Inlines.Clear();

        if (GetSpans(block) is not { } spans)
        {
            return;
        }

        var brush = GetChangedBrush(block);

        foreach (var span in spans)
        {
            var run = new Run(span.Text);

            if (span.Changed && brush is not null)
            {
                run.Background = brush;
            }

            block.Inlines.Add(run);
        }
    }
}
