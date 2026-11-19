using KeepShell.Diagnostics;
using System.Diagnostics.Tracing;

namespace SpaceSnoop.Wpf.Diagnostics;

public sealed class DiagnosticsTrace : IDisposable
{
    private readonly PerformanceMonitor _monitor;

    public DiagnosticsTrace(PerformanceMonitor monitor)
    {
        _monitor = monitor;
        _monitor.Updated += OnUpdated;
    }

    public void Dispose()
    {
        _monitor.Updated -= OnUpdated;
    }

    private void OnUpdated(object? sender, EventArgs e)
    {
        var trace = SpaceSnoopDiagnosticsEvents.Log;

        if (!trace.IsEnabled())
        {
            return;
        }

        var snapshot = _monitor.Snapshot;
        trace.UiSample(snapshot.UiDelayMs, snapshot.Phase ?? string.Empty, snapshot.Gen0Collections, snapshot.Gen1Collections, snapshot.Gen2Collections);
    }
}

[EventSource(Name = "SpaceSnoop-Diagnostics")]
internal sealed class SpaceSnoopDiagnosticsEvents : EventSource
{
    public static readonly SpaceSnoopDiagnosticsEvents Log = new();

    private SpaceSnoopDiagnosticsEvents()
    {
    }

    [Event(1, Level = EventLevel.Informational)]
    public void UiSample(double delayMs, string phase, int gen0, int gen1, int gen2)
    {
        WriteEvent(1, delayMs, phase, gen0, gen1, gen2);
    }
}
