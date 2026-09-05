using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceSnoop.Wpf.Agent;

public sealed class ClaudeAgentBackend : AgentBackendBase
{
    private readonly ILogger<ClaudeAgentBackend> _logger;

    public ClaudeAgentBackend(AgentPreferences preferences, ILogger<ClaudeAgentBackend> logger)
        : base(preferences, logger)
    {
        _logger = logger;
    }

    public override AgentBackendKind Kind => AgentBackendKind.Claude;

    public override string DisplayName => "Claude Code";

    public override string CliName => "claude";

    public override bool HasBuiltInShell => false;

    public override bool SendsSystemPromptEachTurn => true;

    public override string MissingCliHint => "установите его (npm install -g @anthropic-ai/claude-code) и укажите путь в настройках, если он не попадает в PATH.";

    protected override IReadOnlyList<string> ExtraDirectories
    {
        get
        {
            var directories = new List<string>();
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!string.IsNullOrEmpty(userProfile))
            {
                directories.Add(Path.Combine(userProfile, ".local", "bin"));
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            if (!string.IsNullOrEmpty(appData))
            {
                directories.Add(Path.Combine(appData, "npm"));
            }

            return directories;
        }
    }

    protected override AgentLaunch CreateLaunch(AgentRequest request)
    {
        var configPath = request.Mcp is null ? null : TempConfigPath("json");

        return new()
        {
            Arguments = BuildArguments(request, configPath),
            Stdin = request.TurnText,
            TempFiles = configPath is null || request.Mcp is null ? [] : [new(configPath, BuildMcpConfigJson(request.Mcp))],
        };
    }

    protected override IAgentStreamParser CreateParser()
    {
        return new StreamParser();
    }

    protected override void InspectLine(string line)
    {
        if (!line.Contains("mcp_servers", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var (name, status) in ParseDisconnectedMcpServers(line))
        {
            _logger.AgentMcpServerUnavailable(name, status);
        }
    }

    internal static IReadOnlyList<string> BuildArguments(AgentRequest request, string? mcpConfigPath)
    {
        var args = new List<string>
        {
            "--print",
            "--output-format", "stream-json",
            "--verbose",
            "--include-partial-messages",
            "--permission-mode", "dontAsk",
            "--tools", string.Empty,
            "--setting-sources", string.Empty,
            "--disable-slash-commands",
            "--system-prompt", request.SystemPrompt ?? AgentPrompt.System,
        };

        if (mcpConfigPath is not null)
        {
            args.Add("--mcp-config");
            args.Add(mcpConfigPath);
            args.Add("--strict-mcp-config");
        }

        if (request.Mcp is { } mcp)
        {
            if (mcp.AllowedTools.Count > 0)
            {
                args.Add("--allowed-tools");
                args.Add(string.Join(",", mcp.AllowedTools.Select(tool => $"mcp__{mcp.ServerName}__{tool}")));
            }

            if (mcp.DeniedTools.Count > 0)
            {
                args.Add("--disallowed-tools");
                args.Add(string.Join(",", mcp.DeniedTools.Select(tool => $"mcp__{mcp.ServerName}__{tool}")));
            }
        }

        if (!string.IsNullOrEmpty(request.Model))
        {
            args.Add("--model");
            args.Add(request.Model);
        }

        if (!string.IsNullOrEmpty(request.Effort))
        {
            args.Add("--effort");
            args.Add(request.Effort);
        }

        if (!string.IsNullOrEmpty(request.ResumeSessionId))
        {
            args.Add("--resume");
            args.Add(request.ResumeSessionId);
        }

        return args;
    }

    internal static AgentEvent? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("type", out var typeProperty) || typeProperty.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return typeProperty.GetString() switch
            {
                "system" => ParseSystem(root),
                "stream_event" => ParseStreamEvent(root),
                "assistant" => ParseAssistant(root),
                "result" => ParseResult(root),
                _ => null,
            };
        }
    }

    internal static IReadOnlyList<(string Name, string Status)> ParseDisconnectedMcpServers(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("mcp_servers", out var servers)
                || servers.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var disconnected = new List<(string, string)>();

            foreach (var server in servers.EnumerateArray())
            {
                var name = server.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String
                    ? nameProperty.GetString() ?? string.Empty
                    : string.Empty;

                var status = server.TryGetProperty("status", out var statusProperty) && statusProperty.ValueKind == JsonValueKind.String
                    ? statusProperty.GetString() ?? string.Empty
                    : string.Empty;

                if (status != "connected")
                {
                    disconnected.Add((name, status));
                }
            }

            return disconnected;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static string BuildMcpConfigJson(AgentMcpConfig mcp)
    {
        var entry = new McpConfigServer("http", mcp.Endpoint, new Dictionary<string, string> { ["Authorization"] = $"Bearer {mcp.Token}" });
        var payload = new Dictionary<string, Dictionary<string, McpConfigServer>>
        {
            ["mcpServers"] = new() { [mcp.ServerName] = entry },
        };

        return JsonSerializer.Serialize(payload);
    }

    private static AgentEvent? ParseSystem(JsonElement root)
    {
        if (!root.TryGetProperty("subtype", out var subtype)
            || subtype.ValueKind != JsonValueKind.String
            || subtype.GetString() != "init")
        {
            return null;
        }

        return root.TryGetProperty("session_id", out var sessionId) && sessionId.ValueKind == JsonValueKind.String
            ? AgentEvent.Begin(sessionId.GetString() ?? string.Empty)
            : null;
    }

    private static AgentEvent? ParseStreamEvent(JsonElement root)
    {
        if (!root.TryGetProperty("event", out var streamEvent) || streamEvent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!streamEvent.TryGetProperty("type", out var eventType) || eventType.GetString() != "content_block_delta")
        {
            return null;
        }

        if (!streamEvent.TryGetProperty("delta", out var delta)
            || !delta.TryGetProperty("type", out var deltaType)
            || deltaType.GetString() != "text_delta")
        {
            return null;
        }

        return delta.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? AgentEvent.Chunk(text.GetString() ?? string.Empty)
            : null;
    }

    // TODO: если модель зовёт несколько инструментов в одном сообщении, берём только первый – ParseLine
    //       отдаёт максимум одно событие на строку (контракт, который проверяют тесты); апгрейд –
    //       смена сигнатуры на IReadOnlyList<AgentEvent>, если такой случай реально встретится.
    private static AgentEvent? ParseAssistant(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var blockType)
                && blockType.GetString() == "tool_use"
                && block.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String)
            {
                return AgentEvent.Tool(name.GetString() ?? string.Empty, ReadArguments(block, "input"));
            }
        }

        return null;
    }

    private static AgentEvent? ParseResult(JsonElement root)
    {
        var sessionId = root.TryGetProperty("session_id", out var sessionIdProperty) && sessionIdProperty.ValueKind == JsonValueKind.String
            ? sessionIdProperty.GetString()
            : null;

        var isError = root.TryGetProperty("is_error", out var isErrorProperty) && isErrorProperty.ValueKind == JsonValueKind.True;

        if (!isError)
        {
            var costUsd = root.TryGetProperty("total_cost_usd", out var costProperty) && costProperty.ValueKind == JsonValueKind.Number
                ? costProperty.GetDouble()
                : 0;

            return AgentEvent.Done(sessionId, costUsd);
        }

        var reason = root.TryGetProperty("result", out var resultProperty) && resultProperty.ValueKind == JsonValueKind.String
            ? resultProperty.GetString()
            : null;

        return AgentEvent.Fail(reason ?? "Агент вернул ошибку.");
    }

    private sealed class StreamParser : IAgentStreamParser
    {
        public AgentEvent? Parse(string line)
        {
            return ParseLine(line);
        }
    }

    private sealed record McpConfigServer(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("headers")] Dictionary<string, string> Headers);
}
