using System.Globalization;
using System.IO;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Views.Behaviors;

public static class PathTrimmer
{
    private const string Ellipsis = "…";

    public static readonly DependencyProperty FullPathProperty = DependencyProperty.RegisterAttached("FullPath",
        typeof(string),
        typeof(PathTrimmer),
        new(string.Empty, OnFullPathChanged));

    public static string GetFullPath(DependencyObject element)
    {
        return (string)element.GetValue(FullPathProperty);
    }

    public static void SetFullPath(DependencyObject element, string value)
    {
        element.SetValue(FullPathProperty, value);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged && sender is TextBlock textBlock)
        {
            Apply(textBlock);
        }
    }

    private static void OnFullPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
        {
            return;
        }

        textBlock.SizeChanged -= OnSizeChanged;
        textBlock.SizeChanged += OnSizeChanged;

        Apply(textBlock);
    }

    private static void Apply(TextBlock textBlock)
    {
        var full = GetFullPath(textBlock) ?? string.Empty;
        var available = textBlock.ActualWidth;

        if (available <= 1 || string.IsNullOrEmpty(full))
        {
            textBlock.Text = full;
            return;
        }

        textBlock.Text = MeasureWidth(textBlock, full) <= available
            ? full
            : Shorten(textBlock, full, available);
    }

    private static string Shorten(TextBlock textBlock, string path, double available)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (parts.Length <= 2)
        {
            return TrimMiddleByChars(textBlock, path, available);
        }

        var root = parts[0];
        var tail = parts[^1];
        var best = $"{root}{Path.DirectorySeparatorChar}{Ellipsis}{Path.DirectorySeparatorChar}{tail}";

        var accumulated = tail;

        for (var i = parts.Length - 2; i >= 1; i--)
        {
            var candidateTail = $"{parts[i]}{Path.DirectorySeparatorChar}{accumulated}";
            var candidate = $"{root}{Path.DirectorySeparatorChar}{Ellipsis}{Path.DirectorySeparatorChar}{candidateTail}";

            if (MeasureWidth(textBlock, candidate) > available)
            {
                break;
            }

            accumulated = candidateTail;
            best = candidate;
        }

        if (MeasureWidth(textBlock, best) <= available)
        {
            return best;
        }

        path = best;

        return TrimMiddleByChars(textBlock, path, available);
    }

    private static string TrimMiddleByChars(TextBlock textBlock, string text, double available)
    {
        var low = 0;
        var high = text.Length;
        var result = Ellipsis;

        while (low <= high)
        {
            var keep = (low + high) / 2;
            var head = keep / 2;
            var tail = keep - head;
            var candidate = string.Concat(text.AsSpan(0, head), Ellipsis, text.AsSpan(text.Length - tail));

            if (MeasureWidth(textBlock, candidate) <= available)
            {
                result = candidate;
                low = keep + 1;
            }
            else
            {
                high = keep - 1;
            }
        }

        return result;
    }

    private static double MeasureWidth(TextBlock textBlock, string text)
    {
        var typeface = new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(textBlock).PixelsPerDip;

        var formatted = new FormattedText(text,
            CultureInfo.CurrentUICulture,
            textBlock.FlowDirection,
            typeface,
            textBlock.FontSize,
            Brushes.Black,
            pixelsPerDip);

        return formatted.Width;
    }
}
