using KeepShell.Services.Platform;

namespace SpaceSnoop.Wpf.Tests;

internal sealed class FakeUiTimer(TimeSpan interval, Action tick) : IUiTimer
{
    public TimeSpan Interval { get; set; } = interval;

    public bool IsRunning { get; private set; }

    public int Starts { get; private set; }

    public void Start()
    {
        IsRunning = true;
        Starts++;
    }

    public void Stop()
    {
        IsRunning = false;
    }

    public void Tick()
    {
        tick();
    }
}
