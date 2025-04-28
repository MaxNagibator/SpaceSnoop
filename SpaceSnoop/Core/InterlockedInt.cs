namespace SpaceSnoop.Core;

public struct InterlockedInt(int initValue)
{
    private int _value = initValue;

    public int Value => _value;

    public int Inc()
    {
        return Interlocked.Increment(ref _value);
    }

    public int Dec()
    {
        return Interlocked.Decrement(ref _value);
    }
}
