using Microsoft.Extensions.Logging;

namespace SpaceSnoop.Tests;

internal sealed record LogEntry(LogLevel Level, int EventId, Exception? Exception, string Message);

internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly Lock _lock = new();
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_lock)
        {
            _entries.Add(new(logLevel, eventId.Id, exception, formatter(state, exception)));
        }
    }
}
