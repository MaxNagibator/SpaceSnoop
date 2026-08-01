using System.Collections.Immutable;
using System.Diagnostics;

namespace SpaceSnoop.Wpf.Diagnostics;

internal sealed class PerformanceHistoryBuffer(int capacity)
{
    private readonly PerformanceSample[] _samples = new PerformanceSample[Math.Max(1, capacity)];

    private int _count;

    private int _next;

    public int Count => _count;

    public int Capacity => _samples.Length;

    public void Add(PerformanceSample sample)
    {
        _samples[_next] = sample;
        _next = (_next + 1) % _samples.Length;

        if (_count < _samples.Length)
        {
            _count++;
        }
    }

    public void Clear()
    {
        Array.Clear(_samples);
        _count = 0;
        _next = 0;
    }

    public PerformanceHistory Capture(long now, DateTime capturedAtUtc, TimeSpan since, int maxPoints)
    {
        var total = CountWithin(now, since);

        if (total == 0 || maxPoints <= 0)
        {
            return PerformanceHistory.Empty with { CapturedAtUtc = capturedAtUtc };
        }

        var bucket = ((total - 1) / maxPoints) + 1;
        var builder = ImmutableArray.CreateBuilder<PerformancePoint>(Math.Min(total, maxPoints));
        var newest = At(0);
        var oldest = newest;
        var worst = newest;
        var taken = 0;

        for (var offset = 0; offset < total; offset++)
        {
            var sample = At(offset);
            oldest = sample;

            if (taken == 0 || sample.UiDelayMs > worst.UiDelayMs)
            {
                worst = sample;
            }

            taken++;

            if (taken < bucket && offset < total - 1)
            {
                continue;
            }

            builder.Add(Project(worst, now));
            taken = 0;
        }

        builder.Reverse();

        return new(capturedAtUtc,
            Stopwatch.GetElapsedTime(oldest.Timestamp, newest.Timestamp).TotalSeconds,
            newest.Gen0Collections - oldest.Gen0Collections,
            newest.Gen1Collections - oldest.Gen1Collections,
            newest.Gen2Collections - oldest.Gen2Collections,
            total - builder.Count,
            builder.ToImmutable());
    }

    private int CountWithin(long now, TimeSpan since)
    {
        if (since <= TimeSpan.Zero)
        {
            return _count;
        }

        for (var offset = 0; offset < _count; offset++)
        {
            if (Stopwatch.GetElapsedTime(At(offset).Timestamp, now) > since)
            {
                return offset;
            }
        }

        return _count;
    }

    private PerformanceSample At(int offset)
    {
        return _samples[(_next - 1 - offset + _samples.Length) % _samples.Length];
    }

    private static PerformancePoint Project(PerformanceSample sample, long now)
    {
        return new(Stopwatch.GetElapsedTime(sample.Timestamp, now).TotalMilliseconds,
            sample.UiDelayMs,
            sample.ManagedBytes,
            sample.WorkingSetBytes,
            sample.Gen0Collections,
            sample.Gen1Collections,
            sample.Gen2Collections,
            sample.Operation);
    }
}
