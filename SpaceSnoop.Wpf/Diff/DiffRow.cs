namespace SpaceSnoop.Wpf.Diff;

public sealed record DiffRow(
    DiffLineKind LeftKind,
    string LeftNumberText,
    string LeftText,
    DiffLineKind RightKind,
    string RightNumberText,
    string RightText)
{
    public IReadOnlyList<DiffSpan> LeftSpans => TextDiff.HighlightInline(this).Left;

    public IReadOnlyList<DiffSpan> RightSpans => TextDiff.HighlightInline(this).Right;
}
