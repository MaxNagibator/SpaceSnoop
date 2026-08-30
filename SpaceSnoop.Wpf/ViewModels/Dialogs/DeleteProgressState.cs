namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

internal readonly record struct DeleteTick(
    int Index,
    DeleteRowState State,
    string? Error,
    long Freed,
    int Completed,
    int Failed);

internal readonly record struct DeleteProgressSnapshot(
    IReadOnlyList<DeleteTick> Updates,
    int CurrentIndex,
    long Freed,
    int Completed,
    int Failed);

internal sealed class DeleteProgressState
{
    private readonly object _gate = new();
    private readonly DeleteTick[] _pending;
    private readonly bool[] _dirty;
    private readonly Queue<int> _queue;

    private int _currentIndex = -1;
    private long _freed;
    private int _completed;
    private int _failed;

    public DeleteProgressState(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);

        _pending = new DeleteTick[capacity];
        _dirty = new bool[capacity];
        _queue = new(Math.Min(capacity, 1024));
    }

    public void Report(DeleteTick update)
    {
        if ((uint)update.Index >= (uint)_pending.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(update));
        }

        lock (_gate)
        {
            _pending[update.Index] = update;

            if (!_dirty[update.Index])
            {
                _dirty[update.Index] = true;
                _queue.Enqueue(update.Index);
            }

            _currentIndex = update.Index;
            _freed = update.Freed;
            _completed = update.Completed;
            _failed = update.Failed;
        }
    }

    public DeleteProgressSnapshot CreateSnapshot()
    {
        lock (_gate)
        {
            if (_queue.Count == 0)
            {
                return new([], _currentIndex, _freed, _completed, _failed);
            }

            var updates = new DeleteTick[_queue.Count];
            var position = 0;

            while (_queue.TryDequeue(out var index))
            {
                _dirty[index] = false;
                updates[position++] = _pending[index];
            }

            return new(updates, _currentIndex, _freed, _completed, _failed);
        }
    }
}
