using KeepShell.Diagnostics;
using SpaceSnoop.Wpf.Diagnostics;
using System.Diagnostics.Tracing;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[NonParallelizable]
public sealed class DiagnosticsTraceTests
{
    [Test]
    public void Тик_UI_попадает_в_трассу_с_фазой_и_GC_а_после_Dispose_подписка_снята()
    {
        var phase = Guid.NewGuid().ToString("N");
        using var listener = new SampleListener(phase);
        var dispatcher = new FakeUiDispatcher();
        using var monitor = new PerformanceMonitor(new(), dispatcher);
        var trace = new DiagnosticsTrace(monitor);
        monitor.SetPhase(phase);
        monitor.Start();
        var before = listener.Samples.Count;
        dispatcher.Timers.Single().Tick();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(listener.Samples.Count, Is.EqualTo(before + 1));
            Assert.That(listener.Samples[^1][0], Is.EqualTo(monitor.Snapshot.UiDelayMs));
            Assert.That(listener.Samples[^1][2], Is.EqualTo(monitor.Snapshot.Gen0Collections));
            Assert.That(listener.Samples[^1][3], Is.EqualTo(monitor.Snapshot.Gen1Collections));
            Assert.That(listener.Samples[^1][4], Is.EqualTo(monitor.Snapshot.Gen2Collections));
        }

        trace.Dispose();
        dispatcher.Timers.Single().Tick();
        Assert.That(listener.Samples.Count, Is.EqualTo(before + 1));
    }

    private sealed class SampleListener : EventListener
    {
        private readonly string _phase;

        public List<object?[]> Samples { get; } = [];

        public SampleListener(string phase)
        {
            _phase = phase;
            EnableEvents(SpaceSnoopDiagnosticsEvents.Log, EventLevel.Informational);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventId == 1 && eventData.Payload?[1] is string phase && phase == _phase)
            {
                Samples.Add(eventData.Payload.ToArray());
            }
        }
    }
}
