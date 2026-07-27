using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SpaceSnoop.Wpf.Agent;

public sealed class OpenCodeAgentBackend : AgentBackendBase
{
    internal const string ConfigVariable = "OPENCODE_CONFIG";

    internal const string AgentName = "spacesnoop";

    internal static readonly string[] BuiltInTools =
    [
        "bash",
        "edit",
        "write",
        "patch",
        "read",
        "glob",
        "grep",
        "list",
        "task",
        "todowrite",
        "todoread",
        "webfetch",
        "websearch",
        "skill",
        "question",
    ];

    private const string SessionTitle = "SpaceSnoop";

    private static readonly JsonSerializerOptions ConfigJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public OpenCodeAgentBackend(AgentPreferences preferences, ILogger<OpenCodeAgentBackend> logger)
        : base(preferences, logger)
    {
    }

    public override AgentBackendKind Kind => AgentBackendKind.OpenCode;

    public override string DisplayName => "OpenCode";

    public override string CliName => "opencode";

    public override bool HasBuiltInShell => false;

    public override bool SendsSystemPromptEachTurn => true;

    public override string MissingCliHint => "установите его (npm install -g opencode-ai) и укажите путь в настройках, если он не попадает в PATH.";

    public override string ModelHint => "OpenCode ждёт слаг в формате provider/model. Список выше заполняет кнопка обновления – CLI отдаёт только те модели, которые доступны вашей авторизации. Пусто – модель, выбранная по умолчанию в самом CLI.";

    protected override IReadOnlyList<string> ExtraDirectories
    {
        get
        {
            var directories = new List<string>();
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            if (!string.IsNullOrEmpty(appData))
            {
                directories.Add(Path.Combine(appData, "npm", "node_modules", "opencode-ai", "bin"));
                directories.Add(Path.Combine(appData, "npm"));
            }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!string.IsNullOrEmpty(userProfile))
            {
                directories.Add(Path.Combine(userProfile, ".opencode", "bin"));
                directories.Add(Path.Combine(userProfile, ".local", "bin"));
            }

            return directories;
        }
    }

    public override IReadOnlyList<AgentModelOption> LoadModels()
    {
        return Detect() is { } cli
            ? AgentModels.ParseOpenCodeModels(AgentCli.Run(cli.ExecutablePath, "models", "--pure", "--verbose"))
            : [];
    }

    protected override AgentLaunch CreateLaunch(AgentRequest request)
    {
        var configPath = TempConfigPath("json");
        var environment = new Dictionary<string, string> { [ConfigVariable] = configPath };

        if (request.Mcp is { } mcp)
        {
            environment[TokenVariable] = mcp.Token;
        }

        return new()
        {
            Arguments = BuildArguments(request),
            Stdin = request.Prompt,
            Environment = environment,
            TempFiles = [new(configPath, BuildConfigJson(request))],
        };
    }

    protected override IAgentStreamParser CreateParser()
    {
        return new StreamParser();
    }

    internal static IReadOnlyList<string> BuildArguments(AgentRequest request)
    {
        var args = new List<string> { "run", "--format", "json", "--pure", "--agent", AgentName };

        if (!string.IsNullOrEmpty(request.ResumeSessionId))
        {
            args.Add("--session");
            args.Add(request.ResumeSessionId);
        }
        else
        {
            args.Add("--title");
            args.Add(SessionTitle);
        }

        if (!string.IsNullOrEmpty(request.Model))
        {
            args.Add("--model");
            args.Add(request.Model);
        }

        if (!string.IsNullOrEmpty(request.Effort))
        {
            args.Add("--variant");
            args.Add(request.Effort);
        }

        return args;
    }

    // TODO: OPENCODE_CONFIG подменяет пользовательский opencode.json целиком, поэтому объявленные там
    //       свои провайдеры и baseUrl в ходах приложения не видны (учётные данные из auth.json –
    //       видны); апгрейд – слияние с пользовательским файлом, если такая установка встретится.
    internal static string BuildConfigJson(AgentRequest request)
    {
        var tools = new Dictionary<string, bool>(StringComparer.Ordinal);
        var permission = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var tool in BuiltInTools)
        {
            tools[tool] = false;
            permission[tool] = "deny";
        }

        permission["external_directory"] = "deny";
        permission["doom_loop"] = "deny";

        var config = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["share"] = "disabled",
            ["autoupdate"] = false,
            ["snapshot"] = false,
            ["instructions"] = Array.Empty<string>(),
            ["agent"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [AgentName] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["mode"] = "primary",
                    ["description"] = $"{AgentPersona.Name} – детектив дискового места SpaceSnoop",
                    ["prompt"] = request.SystemPrompt ?? AgentPrompt.System,
                    ["tools"] = tools,
                    ["permission"] = permission,
                },
            },
        };

        if (request.Mcp is { } mcp)
        {
            foreach (var tool in mcp.AllowedTools)
            {
                tools[QualifyTool(mcp.ServerName, tool)] = true;
                permission[QualifyTool(mcp.ServerName, tool)] = "allow";
            }

            foreach (var tool in mcp.DeniedTools)
            {
                tools[QualifyTool(mcp.ServerName, tool)] = false;
                permission[QualifyTool(mcp.ServerName, tool)] = "deny";
            }

            config["mcp"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [mcp.ServerName] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["type"] = "remote",
                    ["enabled"] = true,
                    ["url"] = mcp.Endpoint,
                    ["headers"] = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["Authorization"] = $"Bearer {{env:{TokenVariable}}}",
                    },
                },
            };
        }

        return JsonSerializer.Serialize(config, ConfigJson);
    }

    private static string QualifyTool(string serverName, string tool)
    {
        return $"{serverName}_{tool}";
    }

    internal sealed class StreamParser : IAgentStreamParser
    {
        private readonly HashSet<string> _seenParts = [];

        private string? _sessionId;
        private bool _started;
        private bool _hasText;
        private long _inputTokens;
        private long _outputTokens;
        private double _cost;

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

                _sessionId = Text(root, "sessionID") is { Length: > 0 } sessionId ? sessionId : _sessionId;

                return typeProperty.GetString() switch
                {
                    "step_start" => ParseStart(),
                    "text" => ParseText(root),
                    "tool_use" => ParseToolCall(root),
                    "step_finish" => ParseStepFinish(root),
                    "error" => ParseFailed(root),
                    _ => null,
                };
            }
        }

        public AgentEvent? Complete()
        {
            return _started ? AgentEvent.Done(_sessionId, _cost, _inputTokens + _outputTokens) : null;
        }

        private AgentEvent? ParseStart()
        {
            if (_started)
            {
                return null;
            }

            _started = true;
            return AgentEvent.Begin(_sessionId ?? string.Empty);
        }

        private AgentEvent? ParseText(JsonElement root)
        {
            if (!TryTakePart(root, out var part))
            {
                return null;
            }

            var value = Text(part, "text");

            if (value.Length == 0)
            {
                return null;
            }

            var chunk = _hasText ? string.Concat("\n\n", value) : value;
            _hasText = true;

            return AgentEvent.Chunk(chunk);
        }

        private AgentEvent? ParseToolCall(JsonElement root)
        {
            if (!TryTakePart(root, out var part))
            {
                return null;
            }

            var tool = Text(part, "tool");

            return tool.Length == 0 ? null : AgentEvent.Tool(tool);
        }

        private AgentEvent? ParseStepFinish(JsonElement root)
        {
            if (!root.TryGetProperty("part", out var part) || part.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (part.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Number)
            {
                _cost += cost.GetDouble();
            }

            if (part.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object)
            {
                _inputTokens = Math.Max(_inputTokens, Number(tokens, "input"));
                _outputTokens += Number(tokens, "output") + Number(tokens, "reasoning");
            }

            return null;
        }

        private AgentEvent ParseFailed(JsonElement root)
        {
            var reason = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                ? ReadErrorMessage(error)
                : null;

            FailureHint = reason ?? FailureHint;

            return AgentEvent.Fail(reason ?? "Агент вернул ошибку.");
        }

        private static string? ReadErrorMessage(JsonElement error)
        {
            if (error.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && Text(data, "message") is { Length: > 0 } message)
            {
                return message;
            }

            return Text(error, "name") is { Length: > 0 } name ? name : null;
        }

        private bool TryTakePart(JsonElement root, out JsonElement part)
        {
            if (!root.TryGetProperty("part", out part) || part.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var id = Text(part, "id");

            return id.Length > 0 && _seenParts.Add(id);
        }

        private static long Number(JsonElement element, string property)
        {
            return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetInt64()
                : 0;
        }

        private static string Text(JsonElement element, string property)
        {
            return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }
    }
}
