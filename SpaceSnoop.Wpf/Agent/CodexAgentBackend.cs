using System.IO;
using System.Text.Json;

namespace SpaceSnoop.Wpf.Agent;

public sealed class CodexAgentBackend : AgentBackendBase
{
    internal const string TokenVariable = "SPACESNOOP_MCP_TOKEN";

    private const string SandboxMode = "danger-full-access";

    private readonly ILogger<CodexAgentBackend> _logger;

    public CodexAgentBackend(AgentPreferences preferences, ILogger<CodexAgentBackend> logger)
        : base(preferences, logger)
    {
        _logger = logger;
    }

    public override AgentBackendKind Kind => AgentBackendKind.Codex;

    public override string DisplayName => "Codex";

    public override string CliName => "codex";

    public override bool HasBuiltInShell => true;

    public override bool SendsSystemPromptEachTurn => false;

    public override string MissingCliHint => "установите его (npm install -g @openai/codex) и укажите путь в настройках, если он не попадает в PATH.";

    protected override IReadOnlyList<string> ExtraDirectories
    {
        get
        {
            var directories = new List<string>();
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (!string.IsNullOrEmpty(localAppData))
            {
                directories.Add(Path.Combine(localAppData, "Programs", "OpenAI", "Codex", "bin"));
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            if (!string.IsNullOrEmpty(appData))
            {
                directories.Add(Path.Combine(appData, "npm"));
            }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!string.IsNullOrEmpty(userProfile))
            {
                directories.Add(Path.Combine(userProfile, ".local", "bin"));
            }

            return directories;
        }
    }

    protected override AgentLaunch CreateLaunch(AgentRequest request)
    {
        var environment = new Dictionary<string, string>();

        if (request.Mcp is { } mcp)
        {
            environment[TokenVariable] = mcp.Token;
        }

        return new()
        {
            Arguments = BuildArguments(request),
            Stdin = BuildStdin(request),
            Environment = environment,
        };
    }

    protected override IAgentStreamParser CreateParser()
    {
        return new StreamParser();
    }

    protected override void InspectLine(string line)
    {
        if (ParseStreamError(line) is { } message)
        {
            _logger.AgentStreamError(message);
        }
    }

    internal static string BuildStdin(AgentRequest request)
    {
        var system = request.SystemPrompt;

        return string.IsNullOrEmpty(request.ResumeSessionId) && !string.IsNullOrWhiteSpace(system)
            ? string.Concat(system, "\n\n", request.Prompt)
            : request.Prompt;
    }

    internal static IReadOnlyList<string> BuildArguments(AgentRequest request)
    {
        var args = new List<string> { "exec" };

        if (!string.IsNullOrEmpty(request.ResumeSessionId))
        {
            args.Add("resume");
            args.Add(request.ResumeSessionId);
        }

        args.Add("-");

        args.AddRange([
            "--json",
            "--skip-git-repo-check",
            "--ignore-user-config",
            "--ignore-rules",
            "--strict-config",
        ]);

        AddConfig(args, "sandbox_mode", Quote(SandboxMode));
        AddConfig(args, "approval_policy", Quote("never"));
        AddConfig(args, "tools.web_search", "false");
        AddConfig(args, "project_doc_max_bytes", "0");

        if (request.Mcp is { } mcp)
        {
            var server = $"mcp_servers.{mcp.ServerName}";

            AddConfig(args, $"{server}.url", Quote(mcp.Endpoint));
            AddConfig(args, $"{server}.bearer_token_env_var", Quote(TokenVariable));

            if (mcp.AllowedTools.Count > 0)
            {
                AddConfig(args, $"{server}.enabled_tools", JsonSerializer.Serialize(mcp.AllowedTools));
            }

            if (mcp.DeniedTools.Count > 0)
            {
                AddConfig(args, $"{server}.disabled_tools", JsonSerializer.Serialize(mcp.DeniedTools));
            }
        }

        if (!string.IsNullOrEmpty(request.Model))
        {
            args.Add("--model");
            args.Add(request.Model);
        }

        return args;
    }

    internal static string? ParseStreamError(string line)
    {
        if (!line.Contains("\"error\"", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.GetString() != "error")
            {
                return null;
            }

            return root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void AddConfig(List<string> args, string key, string value)
    {
        args.Add("-c");
        args.Add($"{key}={value}");
    }

    private static string Quote(string value)
    {
        return $"\"{value}\"";
    }

    internal sealed class StreamParser : IAgentStreamParser
    {
        private bool _hasText;

        public string? FailureHint { get; private set; }

        public AgentEvent? Parse(string line)
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

                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("type", out var typeProperty)
                    || typeProperty.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                switch (typeProperty.GetString())
                {
                    case "thread.started":
                        return ParseThreadStarted(root);

                    case "item.started":
                        return ParseToolCall(root);

                    case "item.completed":
                        RememberItemError(root);
                        return ParseMessage(root);

                    case "turn.completed":
                        return ParseCompleted(root);

                    case "turn.failed":
                        return ParseFailed(root);

                    case "error":
                        FailureHint = ReadMessage(root) ?? FailureHint;
                        return null;

                    default:
                        return null;
                }
            }
        }

        private static AgentEvent? ParseThreadStarted(JsonElement root)
        {
            return root.TryGetProperty("thread_id", out var threadId) && threadId.ValueKind == JsonValueKind.String
                ? AgentEvent.Begin(threadId.GetString() ?? string.Empty)
                : null;
        }

        private static AgentEvent? ParseToolCall(JsonElement root)
        {
            if (!TryGetItem(root, out var item, out var itemType))
            {
                return null;
            }

            return itemType switch
            {
                "mcp_tool_call" => item.TryGetProperty("tool", out var tool) && tool.ValueKind == JsonValueKind.String
                    ? AgentEvent.Tool(tool.GetString() ?? string.Empty)
                    : null,
                "command_execution" => AgentEvent.Tool(AgentPrompt.ShellTool),
                _ => null,
            };
        }

        private AgentEvent? ParseMessage(JsonElement root)
        {
            if (!TryGetItem(root, out var item, out var itemType)
                || itemType != "agent_message"
                || !item.TryGetProperty("text", out var text)
                || text.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = text.GetString() ?? string.Empty;

            if (value.Length == 0)
            {
                return null;
            }

            var chunk = _hasText ? string.Concat("\n\n", value) : value;
            _hasText = true;

            return AgentEvent.Chunk(chunk);
        }

        private AgentEvent ParseCompleted(JsonElement root)
        {
            FailureHint = null;

            long tokens = 0;

            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                tokens = ReadTokens(usage, "input_tokens") + ReadTokens(usage, "output_tokens");
            }

            return AgentEvent.Done(sessionId: null, costUsd: 0, tokens);
        }

        private static AgentEvent ParseFailed(JsonElement root)
        {
            var reason = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                ? ReadMessage(error)
                : null;

            return AgentEvent.Fail(reason ?? "Агент вернул ошибку.");
        }

        private static long ReadTokens(JsonElement usage, string name)
        {
            return usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;
        }

        private void RememberItemError(JsonElement root)
        {
            if (TryGetItem(root, out var item, out var itemType) && itemType == "error")
            {
                FailureHint = ReadMessage(item) ?? FailureHint;
            }
        }

        private static string? ReadMessage(JsonElement element)
        {
            return element.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                ? message.GetString()
                : null;
        }

        private static bool TryGetItem(JsonElement root, out JsonElement item, out string? itemType)
        {
            itemType = null;

            if (!root.TryGetProperty("item", out item) || item.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!item.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            itemType = type.GetString();
            return true;
        }
    }
}
