namespace SpaceSnoop.Wpf.Agent;

public enum AgentBackendKind
{
    Claude = 0,
    Codex = 1,
}

public enum AgentEventKind
{
    Started = 0,
    Text = 1,
    ToolCall = 2,
    Completed = 3,
    Failed = 4,
}

public sealed record AgentEvent(AgentEventKind Kind)
{
    public string Text { get; init; } = string.Empty;

    public string? SessionId { get; init; }

    public string? ToolName { get; init; }

    public double CostUsd { get; init; }

    public long Tokens { get; init; }

    public static AgentEvent Begin(string sessionId)
    {
        return new(AgentEventKind.Started) { SessionId = sessionId };
    }

    public static AgentEvent Chunk(string text)
    {
        return new(AgentEventKind.Text) { Text = text };
    }

    public static AgentEvent Tool(string toolName)
    {
        return new(AgentEventKind.ToolCall) { ToolName = toolName };
    }

    public static AgentEvent Done(string? sessionId, double costUsd, long tokens = 0)
    {
        return new(AgentEventKind.Completed) { SessionId = sessionId, CostUsd = costUsd, Tokens = tokens };
    }

    public static AgentEvent Fail(string reason)
    {
        return new(AgentEventKind.Failed) { Text = reason };
    }
}

public sealed record AgentCliInfo(string ExecutablePath, string Version);

public sealed record AgentMcpConfig(
    string ServerName,
    string Endpoint,
    string Token,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<string> DeniedTools);

public sealed record AgentRequest
{
    public required string Prompt { get; init; }

    public string? ResumeSessionId { get; init; }

    public string? SystemPrompt { get; init; }

    public string? Model { get; init; }

    public AgentMcpConfig? Mcp { get; init; }
}

public sealed record AgentTempFile(string Path, string Content);

public sealed record AgentLaunch
{
    public required IReadOnlyList<string> Arguments { get; init; }

    public string Stdin { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<AgentTempFile> TempFiles { get; init; } = [];
}

public interface IAgentStreamParser
{
    AgentEvent? Parse(string line);

    string? FailureHint => null;
}

public interface IAgentBackend
{
    AgentBackendKind Kind { get; }

    string DisplayName { get; }

    string CliName { get; }

    bool HasBuiltInShell { get; }

    bool SendsSystemPromptEachTurn { get; }

    string MissingCliHint { get; }

    AgentCliInfo? Detect();

    IAsyncEnumerable<AgentEvent> RunAsync(AgentRequest request, CancellationToken cancellationToken);
}
