using KeepShell.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace SpaceSnoop.Wpf.Tests;

internal static class TestDiagnostics
{
    public static PerformanceMonitor Monitor()
    {
        return new(new(), new FakeUiDispatcher(), NullLogger<PerformanceMonitor>.Instance);
    }
}
