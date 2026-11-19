using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceSnoop.Wpf.Agent;

public enum AgentTranscriptKind
{
    Launch = 0,
    Config = 1,
    Stdin = 2,
    Stdout = 3,
    Stderr = 4,
    Exit = 5,
    Message = 6,
}

public interface IAgentTranscript : KeepShell.Agents.IAgentTranscript, IDisposable
{
    string Path { get; }

    void Write(AgentTranscriptKind kind, string text);
}

public sealed class AgentTranscript : IAgentTranscript
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };

    private readonly object _gate = new();
    private readonly ILogger _logger;
    private readonly string? _token;

    private StreamWriter? _writer;

    public string Path { get; }

    public AgentTranscript(string path, string? token, ILogger logger)
    {
        Path = path;
        _token = token;
        _logger = logger;
        _writer = new(path, false, new UTF8Encoding(true))
        {
            AutoFlush = true,
        };
    }

    public void Write(AgentTranscriptKind kind, string text)
    {
        lock (_gate)
        {
            if (_writer is not { } writer)
            {
                return;
            }

            try
            {
                var safe = string.IsNullOrEmpty(_token) ? text : text.Replace(_token, "<токен>", StringComparison.Ordinal);
                writer.WriteLine(JsonSerializer.Serialize(new Line(DateTimeOffset.Now, kind, safe), Options));
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or UnauthorizedAccessException)
            {
                _logger.AgentTranscriptFailed(exception, Path);
                Close();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Close();
        }
    }

    void KeepShell.Agents.IAgentTranscript.Write(KeepShell.Agents.AgentTranscriptKind kind, string text)
    {
        Write(kind switch
        {
            KeepShell.Agents.AgentTranscriptKind.Launch => AgentTranscriptKind.Launch,
            KeepShell.Agents.AgentTranscriptKind.Stdout => AgentTranscriptKind.Stdout,
            KeepShell.Agents.AgentTranscriptKind.Stderr => AgentTranscriptKind.Stderr,
            KeepShell.Agents.AgentTranscriptKind.Exit => AgentTranscriptKind.Exit,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        }, text);
    }

    private void Close()
    {
        try
        {
            _writer?.Dispose();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            _logger.AgentTranscriptFailed(exception, Path);
        }

        _writer = null;
    }

    private sealed record Line(DateTimeOffset At, AgentTranscriptKind Kind, string Text);
}
