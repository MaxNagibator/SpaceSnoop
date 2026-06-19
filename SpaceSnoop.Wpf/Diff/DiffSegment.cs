namespace SpaceSnoop.Wpf.Diff;

public sealed record DiffSegment(bool IsGap, int Start, int Count);
