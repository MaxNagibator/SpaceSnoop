using System.Collections.Immutable;
using System.Diagnostics;

namespace SpaceSnoop.Wpf.Diagnostics;

public sealed class PerformanceHistoryBuffer(int capacity)
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
        if (_count == 0 || maxPoints <= 0)
        {
            return PerformanceHistory.Empty with { CapturedAtUtc = capturedAtUtc };
        }

        var builder = ImmutableArray.CreateBuilder<PerformancePoint>(Math.Min(_count, maxPoints));
        var newest = default(PerformanceSample);
        var oldest = default(PerformanceSample);

        for (var offset = 1; offset <= _count && builder.Count < maxPoints; offset++)
        {
            var sample = _samples[(_next - offset + _samples.Length) % _samples.Length];
            var age = Stopwatch.GetElapsedTime(sample.Timestamp, now);

            if (since > TimeSpan.Zero && age > since)
            {
                break;
            }

            if (builder.Count == 0)
            {
                newest = sample;
            }

            oldest = sample;

            builder.Add(new(age.TotalMilliseconds,
                sample.UiDelayMs,
                sample.ManagedBytes,
                sample.WorkingSetBytes,
                sample.Gen0Collections,
                sample.Gen1Collections,
                sample.Gen2Collections,
                sample.Operation));
        }

        if (builder.Count == 0)
        {
            return PerformanceHistory.Empty with { CapturedAtUtc = capturedAtUtc };
        }

        builder.Reverse();

        return new(capturedAtUtc,
            Stopwatch.GetElapsedTime(oldest.Timestamp, newest.Timestamp).TotalSeconds,
            newest.Gen0Collections - oldest.Gen0Collections,
            newest.Gen1Collections - oldest.Gen1Collections,
            newest.Gen2Collections - oldest.Gen2Collections,
            builder.ToImmutable());
    }
}
