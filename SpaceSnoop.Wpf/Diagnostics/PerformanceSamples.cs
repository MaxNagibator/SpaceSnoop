namespace SpaceSnoop.Wpf.Diagnostics;

public sealed class PerformanceSamples(int capacity)
{
    private readonly double[] _values = new double[Math.Max(1, capacity)];

    private int _count;

    private int _next;

    public int Count => _count;

    public double Last { get; private set; }

    public void Add(double value)
    {
        _values[_next] = value;
        _next = (_next + 1) % _values.Length;

        if (_count < _values.Length)
        {
            _count++;
        }

        Last = value;
    }

    public void Clear()
    {
        Array.Clear(_values);
        _count = 0;
        _next = 0;
        Last = 0;
    }

    public double Peak()
    {
        var peak = 0d;

        for (var index = 0; index < _count; index++)
        {
            peak = Math.Max(peak, _values[index]);
        }

        return peak;
    }

    public double SpanSeconds(double intervalMs)
    {
        return _count == 0 ? 0 : _count * (intervalMs + Average()) / 1000;
    }

    public double Average()
    {
        if (_count == 0)
        {
            return 0;
        }

        var sum = 0d;

        for (var index = 0; index < _count; index++)
        {
            sum += _values[index];
        }

        return sum / _count;
    }
}
