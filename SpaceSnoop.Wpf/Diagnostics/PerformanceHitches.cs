using System.Collections.Immutable;

namespace SpaceSnoop.Wpf.Diagnostics;

public readonly record struct PerformanceHitchRow(DateTime TimeUtc, double DelayMs, string? Operation);

public sealed record PerformanceHitches(ImmutableArray<PerformanceHitchRow> Rows, int Total, double SpanSeconds)
{
    public static PerformanceHitches Empty { get; } = new([], 0, 0);
}
