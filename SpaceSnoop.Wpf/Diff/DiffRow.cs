namespace SpaceSnoop.Wpf.Diff;

public sealed record DiffRow(
    DiffLineKind LeftKind,
    string LeftNumberText,
    string LeftText,
    DiffLineKind RightKind,
    string RightNumberText,
    string RightText);
