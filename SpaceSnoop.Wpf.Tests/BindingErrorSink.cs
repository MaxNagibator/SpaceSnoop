using System.Diagnostics;
using System.Globalization;

namespace SpaceSnoop.Wpf.Tests;

internal sealed class BindingErrorSink : TraceListener
{
    private readonly List<string> _errors = [];

    public IReadOnlyList<string> Errors => _errors;

    public void Clear()
    {
        _errors.Clear();
    }

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
    {
        Capture(eventType, message);
    }

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
    {
        Capture(eventType, format is null || args is null ? format : string.Format(CultureInfo.InvariantCulture, format, args));
    }

    public override void Write(string? message)
    {
    }

    public override void WriteLine(string? message)
    {
    }

    private void Capture(TraceEventType eventType, string? message)
    {
        if (eventType > TraceEventType.Error || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _errors.Add(message);
    }
}
