using System.Diagnostics;

namespace SpaceSnoop.Wpf.Diagnostics;

public sealed class PerformanceFrames(double slowMs)
{
    private double _total;

    private long _previous;

    public int Count { get; private set; }

    public int SlowCount { get; private set; }

    public double LastMs { get; private set; }

    public double PeakMs { get; private set; }

    public double AverageMs => Count == 0 ? 0 : _total / Count;

    public void Mark(long timestamp)
    {
        if (_previous != 0)
        {
            var elapsed = Stopwatch.GetElapsedTime(_previous, timestamp).TotalMilliseconds;

            LastMs = elapsed;
            PeakMs = Math.Max(PeakMs, elapsed);
            _total += elapsed;
            Count++;

            if (elapsed >= slowMs)
            {
                SlowCount++;
            }
        }

        _previous = timestamp;
    }

    public void Pause()
    {
        _previous = 0;
    }

    public void Clear()
    {
        _total = 0;
        _previous = 0;
        Count = 0;
        SlowCount = 0;
        LastMs = 0;
        PeakMs = 0;
    }
}
