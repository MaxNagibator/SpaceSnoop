namespace SpaceSnoop.Wpf.Markdown;

public sealed record MarkdownBlock(MarkdownBlockKind Kind, IReadOnlyList<MarkdownSpan> Spans)
{
    public int Level { get; init; }

    public string Marker { get; init; } = string.Empty;

    public string Text => string.Concat(Spans.Select(span => span.Text));
}
