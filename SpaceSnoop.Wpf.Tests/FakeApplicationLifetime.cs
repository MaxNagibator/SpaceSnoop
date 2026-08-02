using KeepShell.Services.Platform;

namespace SpaceSnoop.Wpf.Tests;

internal sealed class FakeApplicationLifetime : IApplicationLifetime
{
    public bool IsMainWindowMinimized { get; set; }

    public int ShutdownCalls { get; private set; }

    public void Shutdown()
    {
        ShutdownCalls++;
    }
}
