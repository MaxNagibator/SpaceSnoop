namespace SpaceSnoop.Core;

public readonly record struct OperationProgress(int Completed, string Current, long Bytes = 0);

public sealed class OperationProgressState : IProgress<OperationProgress>
{
    private int _completed;
    private long _bytes;
    private volatile string _current = string.Empty;

    public void Report(OperationProgress value)
    {
        RaiseTo(ref _completed, value.Completed);
        RaiseTo(ref _bytes, value.Bytes);
        _current = value.Current;
    }

    public OperationProgress CreateSnapshot()
    {
        return new(Volatile.Read(ref _completed), _current, Interlocked.Read(ref _bytes));
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _completed, 0);
        Interlocked.Exchange(ref _bytes, 0);
        _current = string.Empty;
    }

    private static void RaiseTo(ref int target, int value)
    {
        var seen = Volatile.Read(ref target);

        while (value > seen)
        {
            var previous = Interlocked.CompareExchange(ref target, value, seen);

            if (previous == seen)
            {
                return;
            }

            seen = previous;
        }
    }

    private static void RaiseTo(ref long target, long value)
    {
        var seen = Interlocked.Read(ref target);

        while (value > seen)
        {
            var previous = Interlocked.CompareExchange(ref target, value, seen);

            if (previous == seen)
            {
                return;
            }

            seen = previous;
        }
    }
}
