using KeepShell.Services.Platform;

namespace SpaceSnoop.Wpf.Tests;

internal sealed class FakeUiDispatcher : IUiDispatcher
{
    public List<FakeUiTimer> Timers { get; } = [];

    public bool HasAccess => true;

    public void Invoke(Action action)
    {
        action();
    }

    public IUiTimer CreateTimer(TimeSpan interval, Action tick)
    {
        var timer = new FakeUiTimer(interval, tick);
        Timers.Add(timer);

        return timer;
    }
}
