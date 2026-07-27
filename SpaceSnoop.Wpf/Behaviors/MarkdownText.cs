using SpaceSnoop.Wpf.Markdown;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SpaceSnoop.Wpf.Behaviors;

public static class MarkdownText
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached("Source", typeof(string), typeof(MarkdownText), new(null, OnSourceChanged));

    private const double BlockSpacing = 8;

    private const double ListIndent = 18;

    private const double CodePadding = 8;

    public static string? GetSource(DependencyObject element)
    {
        return (string?)element.GetValue(SourceProperty);
    }

    public static void SetSource(DependencyObject element, string? value)
    {
        element.SetValue(SourceProperty, value);
    }

    private static void OnSourceChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not RichTextBox box)
        {
            return;
        }

        box.Document = BuildDocument(GetSource(box) ?? string.Empty);
    }

    private static FlowDocument BuildDocument(string text)
    {
        var document = new FlowDocument { PagePadding = new(0) };
        var blocks = MarkdownParser.Parse(text);
        var index = 0;

        while (index < blocks.Count)
        {
            var block = blocks[index];

            if (block.Kind is MarkdownBlockKind.Bullet or MarkdownBlockKind.Ordered)
            {
                document.Blocks.Add(BuildList(blocks, ref index));
                continue;
            }

            document.Blocks.Add(block.Kind == MarkdownBlockKind.Code ? BuildCode(block) : BuildParagraph(block));
            index++;
        }

        if (document.Blocks.LastBlock is { } last)
        {
            last.Margin = new(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
        }

        return document;
    }

    private static List BuildList(IReadOnlyList<MarkdownBlock> blocks, ref int index)
    {
        var kind = blocks[index].Kind;
        var list = new List
        {
            MarkerStyle = kind == MarkdownBlockKind.Ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new(0, 0, 0, BlockSpacing),
            Padding = new(ListIndent, 0, 0, 0),
            MarkerOffset = 6,
        };

        while (index < blocks.Count && blocks[index].Kind == kind)
        {
            var block = blocks[index];
            var paragraph = new Paragraph { Margin = new(block.Level * ListIndent, 0, 0, 2) };
            AppendSpans(paragraph, block.Spans);
            list.ListItems.Add(new(paragraph));
            index++;
        }

        return list;
    }

    private static Paragraph BuildParagraph(MarkdownBlock block)
    {
        var paragraph = new Paragraph { Margin = new(0, 0, 0, BlockSpacing) };

        if (block.Kind == MarkdownBlockKind.Heading)
        {
            paragraph.SetResourceReference(TextElement.FontWeightProperty, "Font.Weight.SemiBold");
            paragraph.Margin = new(0, block.Level > 1 ? 4 : 0, 0, 4);
        }

        AppendSpans(paragraph, block.Spans);

        return paragraph;
    }

    private static Paragraph BuildCode(MarkdownBlock block)
    {
        var paragraph = new Paragraph(new Run(block.Text))
        {
            Margin = new(0, 0, 0, BlockSpacing),
            Padding = new(CodePadding),
        };

        paragraph.SetResourceReference(TextElement.FontFamilyProperty, "Font.Mono");
        paragraph.SetResourceReference(TextElement.BackgroundProperty, "Bg.SurfaceMute");

        return paragraph;
    }

    private static void AppendSpans(Paragraph paragraph, IReadOnlyList<MarkdownSpan> spans)
    {
        foreach (var span in spans)
        {
            var run = new Run(span.Text);

            if (span.Style.HasFlag(MarkdownSpanStyle.Bold))
            {
                run.SetResourceReference(TextElement.FontWeightProperty, "Font.Weight.SemiBold");
            }

            if (span.Style.HasFlag(MarkdownSpanStyle.Italic))
            {
                run.FontStyle = FontStyles.Italic;
            }

            if (span.Style.HasFlag(MarkdownSpanStyle.Code))
            {
                run.SetResourceReference(TextElement.FontFamilyProperty, "Font.Mono");
                run.SetResourceReference(TextElement.BackgroundProperty, "Bg.SurfaceMute");
            }

            paragraph.Inlines.Add(run);
        }
    }
}
