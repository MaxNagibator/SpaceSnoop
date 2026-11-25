using System.Buffers;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Views.Behaviors;

public static class PathTrimmer
{
    private const string Ellipsis = "…";

    private static readonly char[] PathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
    private static readonly SearchValues<char> PathSeparatorSearch = SearchValues.Create(PathSeparators);
    private static readonly ConditionalWeakTable<TextBlock, TrimState> States = new();

    public static readonly DependencyProperty FullPathProperty = DependencyProperty.RegisterAttached(
        "FullPath",
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

        if (string.IsNullOrEmpty(full))
        {
            SetTextIfChanged(textBlock, string.Empty);
            return;
        }

        if (textBlock.TextTrimming != TextTrimming.None && !full.AsSpan().ContainsAny(PathSeparatorSearch))
        {
            SetTextIfChanged(textBlock, full);
            return;
        }

        var available = textBlock.ActualWidth;

        if (available <= 1)
        {
            SetTextIfChanged(textBlock, full);
            return;
        }

        var fontFamily = textBlock.FontFamily;
        var fontStyle = textBlock.FontStyle;
        var fontWeight = textBlock.FontWeight;
        var fontStretch = textBlock.FontStretch;
        var fontSize = textBlock.FontSize;
        var flowDirection = textBlock.FlowDirection;
        var pixelsPerDip = VisualTreeHelper.GetDpi(textBlock).PixelsPerDip;

        var state = States.GetOrCreateValue(textBlock);

        if (state.Matches(full, available, fontFamily, fontStyle, fontWeight, fontStretch, fontSize, flowDirection, pixelsPerDip)
            && string.Equals(textBlock.Text, state.Result, StringComparison.Ordinal))
        {
            return;
        }

        var typeface = state.GetOrCreateTypeface(fontFamily, fontStyle, fontWeight, fontStretch);
        var result = MeasureWidth(full, typeface, fontSize, flowDirection, pixelsPerDip) <= available
            ? full
            : Shorten(full, available, typeface, fontSize, flowDirection, pixelsPerDip);

        state.Update(full, available, fontSize, flowDirection, pixelsPerDip, result);
        SetTextIfChanged(textBlock, result);
    }

    private static void SetTextIfChanged(TextBlock textBlock, string value)
    {
        if (!string.Equals(textBlock.Text, value, StringComparison.Ordinal))
        {
            textBlock.Text = value;
        }
    }

    private static string Shorten(
        string path,
        double available,
        Typeface typeface,
        double fontSize,
        FlowDirection flowDirection,
        double pixelsPerDip)
    {
        var parts = path.Split(PathSeparators);

        if (parts.Length <= 2)
        {
            return TrimMiddleByChars(path, available, typeface, fontSize, flowDirection, pixelsPerDip);
        }

        var root = parts[0];
        var tail = parts[^1];
        var best = $"{root}{Path.DirectorySeparatorChar}{Ellipsis}{Path.DirectorySeparatorChar}{tail}";

        var accumulated = tail;

        for (var i = parts.Length - 2; i >= 1; i--)
        {
            var candidateTail = $"{parts[i]}{Path.DirectorySeparatorChar}{accumulated}";
            var candidate = $"{root}{Path.DirectorySeparatorChar}{Ellipsis}{Path.DirectorySeparatorChar}{candidateTail}";

            if (MeasureWidth(candidate, typeface, fontSize, flowDirection, pixelsPerDip) > available)
            {
                break;
            }

            accumulated = candidateTail;
            best = candidate;
        }

        if (MeasureWidth(best, typeface, fontSize, flowDirection, pixelsPerDip) <= available)
        {
            return best;
        }

        return TrimMiddleByChars(best, available, typeface, fontSize, flowDirection, pixelsPerDip);
    }

    private static string TrimMiddleByChars(
        string text,
        double available,
        Typeface typeface,
        double fontSize,
        FlowDirection flowDirection,
        double pixelsPerDip)
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

            if (MeasureWidth(candidate, typeface, fontSize, flowDirection, pixelsPerDip) <= available)
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

    private static double MeasureWidth(
        string text,
        Typeface typeface,
        double fontSize,
        FlowDirection flowDirection,
        double pixelsPerDip)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            flowDirection,
            typeface,
            fontSize,
            Brushes.Black,
            pixelsPerDip);

        return formatted.Width;
    }

    private sealed class TrimState
    {
        private FontFamily? _fontFamily;
        private FontStyle _fontStyle;
        private FontWeight _fontWeight;
        private FontStretch _fontStretch;
        private Typeface? _typeface;

        private string? _full;
        private double _available;
        private double _fontSize;
        private FlowDirection _flowDirection;
        private double _pixelsPerDip;

        public string? Result { get; private set; }

        public bool Matches(
            string full,
            double available,
            FontFamily fontFamily,
            FontStyle fontStyle,
            FontWeight fontWeight,
            FontStretch fontStretch,
            double fontSize,
            FlowDirection flowDirection,
            double pixelsPerDip)
        {
            return Result is not null
                && string.Equals(_full, full, StringComparison.Ordinal)
                && BitConverter.DoubleToInt64Bits(_available) == BitConverter.DoubleToInt64Bits(available)
                && BitConverter.DoubleToInt64Bits(_fontSize) == BitConverter.DoubleToInt64Bits(fontSize)
                && _flowDirection == flowDirection
                && BitConverter.DoubleToInt64Bits(_pixelsPerDip) == BitConverter.DoubleToInt64Bits(pixelsPerDip)
                && _fontStyle == fontStyle
                && _fontWeight == fontWeight
                && _fontStretch == fontStretch
                && Equals(_fontFamily, fontFamily);
        }

        public Typeface GetOrCreateTypeface(FontFamily fontFamily, FontStyle fontStyle, FontWeight fontWeight, FontStretch fontStretch)
        {
            if (_typeface is not null
                && _fontStyle == fontStyle
                && _fontWeight == fontWeight
                && _fontStretch == fontStretch
                && Equals(_fontFamily, fontFamily))
            {
                return _typeface;
            }

            _fontFamily = fontFamily;
            _fontStyle = fontStyle;
            _fontWeight = fontWeight;
            _fontStretch = fontStretch;
            _typeface = new(fontFamily, fontStyle, fontWeight, fontStretch);
            return _typeface;
        }

        public void Update(
            string full,
            double available,
            double fontSize,
            FlowDirection flowDirection,
            double pixelsPerDip,
            string result)
        {
            _full = full;
            _available = available;
            _fontSize = fontSize;
            _flowDirection = flowDirection;
            _pixelsPerDip = pixelsPerDip;
            Result = result;
        }
    }
}
