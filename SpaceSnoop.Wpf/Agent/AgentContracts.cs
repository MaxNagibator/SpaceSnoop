namespace SpaceSnoop.Wpf.Agent;

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

    public static AgentEvent Done(string? sessionId, double costUsd)
    {
        return new(AgentEventKind.Completed) { SessionId = sessionId, CostUsd = costUsd };
    }

    public static AgentEvent Fail(string reason)
    {
        return new(AgentEventKind.Failed) { Text = reason };
    }
}

public sealed record AgentCliInfo(string ExecutablePath, string Version);

/// <param name="AllowedTools">Короткие имена, без префикса: полное имя <c>mcp__{ServerName}__{tool}</c> складывает бэкенд.</param>
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

public interface IAgentBackend
{
    string Id { get; }

    string DisplayName { get; }

    AgentCliInfo? Detect();

    IAsyncEnumerable<AgentEvent> RunAsync(AgentRequest request, CancellationToken cancellationToken);
}
