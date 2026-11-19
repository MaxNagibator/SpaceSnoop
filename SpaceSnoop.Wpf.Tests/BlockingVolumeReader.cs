using SpaceSnoop.Wpf.Bootstrap.Platform;

namespace SpaceSnoop.Wpf.Tests;

internal sealed class BlockingVolumeReader : IDisposable
{
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(10);

    private readonly Func<string, long?> _original = VolumeSpace.Reader;
    private readonly ManualResetEventSlim _entered = new();
    private readonly ManualResetEventSlim _release = new();
    private readonly long _free;
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public BlockingVolumeReader(long free)
    {
        _free = free;
        VolumeSpace.Reader = Read;
    }

    public void WaitEntered()
    {
        Assert.That(_entered.Wait(Ceiling), Is.True, "замер места так и не начался");
    }

    public void Release()
    {
        _release.Set();
    }

    public void Dispose()
    {
        _release.Set();
        VolumeSpace.Reader = _original;
    }

    private long? Read(string path)
    {
        Interlocked.Increment(ref _calls);
        _entered.Set();
        _release.Wait(Ceiling);
        return _free;
    }
}
