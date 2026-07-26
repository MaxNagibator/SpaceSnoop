using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceSnoop.Wpf.Agent;

public sealed class ClaudeAgentBackend : IAgentBackend, IDisposable
{
    private readonly AgentPreferences _preferences;
    private readonly ILogger<ClaudeAgentBackend> _logger;

    private readonly HashSet<Process> _live = [];

    private bool _cacheValid;
    private string _cachedForCliPath = string.Empty;
    private AgentCliInfo? _cached;

    public ClaudeAgentBackend(AgentPreferences preferences, ILogger<ClaudeAgentBackend> logger)
    {
        _preferences = preferences;
        _logger = logger;
    }

    public string Id => "claude";

    public string DisplayName => "Claude Code";

    public void Dispose()
    {
        Process[] live;

        lock (_live)
        {
            live = [.. _live];
            _live.Clear();
        }

        foreach (var process in live)
        {
            TryKill(process);
        }
    }

    public AgentCliInfo? Detect()
    {
        var overridePath = _preferences.CliPath;

        if (_cacheValid && _cachedForCliPath == overridePath)
        {
            return _cached;
        }

        _cached = AgentCli.Detect(overridePath);
        _cachedForCliPath = overridePath;
        _cacheValid = true;

        if (_cached is not null)
        {
            _logger.AgentCliDetected(_cached.ExecutablePath, _cached.Version);
        }
        else
        {
            _logger.AgentCliMissing();
        }

        return _cached;
    }

    public async IAsyncEnumerable<AgentEvent> RunAsync(AgentRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cli = await Task.Run(Detect, cancellationToken).ConfigureAwait(false);

        if (cli is null)
        {
            const string reason = "CLI Claude Code не найден на машине – установите его (npm install -g @anthropic-ai/claude-code) и укажите путь в настройках, если он не попадает в PATH.";
            _logger.AgentTurnFailed(null, reason);
            yield return AgentEvent.Fail(reason);
            yield break;
        }

        string? mcpConfigPath = null;
        Process? process = null;
        var stderrTail = new List<string>();
        var resultYielded = false;
        var stopwatch = Stopwatch.StartNew();

        _logger.AgentTurnStarted(request.Mcp?.AllowedTools.Count ?? 0, !string.IsNullOrEmpty(request.ResumeSessionId));

        try
        {
            if (request.Mcp is not null)
            {
                mcpConfigPath = Path.Combine(Path.GetTempPath(), $"spacesnoop-mcp-{Guid.NewGuid():N}.json");
                await File.WriteAllTextAsync(mcpConfigPath, BuildMcpConfigJson(request.Mcp), cancellationToken).ConfigureAwait(false);
            }

            var info = BuildProcessStartInfo(cli.ExecutablePath, BuildArguments(request, mcpConfigPath));

            try
            {
                process = Process.Start(info);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                _logger.AgentTurnFailed(exception, "не удалось запустить процесс CLI");
                process = null;
            }

            if (process is null)
            {
                const string reason = "Не удалось запустить CLI Claude Code.";
                yield return AgentEvent.Fail(reason);
                yield break;
            }

            lock (_live)
            {
                _live.Add(process);
            }

            using var kill = cancellationToken.Register(() => TryKill(process));

            var mcpToken = request.Mcp?.Token;
            var stderrTask = DrainStderrAsync(process, stderrTail, mcpToken);

            Exception? stdinFailure = null;

            try
            {
                await process.StandardInput.WriteAsync(request.Prompt).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException exception)
            {
                stdinFailure = exception;
            }

            if (stdinFailure is not null)
            {
                await stderrTask.ConfigureAwait(false);

                var reason = BuildExitReason(process, stderrTail);
                _logger.AgentTurnFailed(stdinFailure, reason);
                yield return AgentEvent.Fail(reason);
                yield break;
            }

            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                if (line is null)
                {
                    break;
                }

                var agentEvent = ParseLine(line);

                if (agentEvent is null)
                {
                    continue;
                }

                switch (agentEvent.Kind)
                {
                    case AgentEventKind.Started:
                        LogDisconnectedMcpServers(line);
                        break;

                    case AgentEventKind.ToolCall:
                        _logger.AgentToolInvoked(agentEvent.ToolName ?? string.Empty);
                        break;

                    case AgentEventKind.Completed:
                        resultYielded = true;
                        _logger.AgentTurnCompleted(stopwatch.ElapsedMilliseconds, agentEvent.CostUsd);
                        break;

                    case AgentEventKind.Failed:
                        resultYielded = true;
                        _logger.AgentTurnFailed(null, agentEvent.Text);
                        break;
                }

                yield return agentEvent;
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);

            if (!resultYielded)
            {
                var reason = BuildExitReason(process, stderrTail);
                _logger.AgentTurnFailed(null, reason);
                yield return AgentEvent.Fail(reason);
            }
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.AgentTurnCancelled();
            }

            if (process is not null)
            {
                lock (_live)
                {
                    _live.Remove(process);
                }

                TryKill(process);
                process.Dispose();
            }

            if (mcpConfigPath is not null)
            {
                try
                {
                    File.Delete(mcpConfigPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static string BuildExitReason(Process process, IReadOnlyList<string> stderrTail)
    {
        var exitCode = process.HasExited ? process.ExitCode : -1;

        return stderrTail.Count > 0
            ? $"CLI Claude Code завершился с кодом {exitCode}: {string.Join(" ", stderrTail)}"
            : $"CLI Claude Code завершился с кодом {exitCode} без ответа.";
    }

    internal static string RedactToken(string text, string? token)
    {
        return string.IsNullOrEmpty(token) ? text : text.Replace(token, "<токен>");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
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
                return AgentEvent.Tool(name.GetString() ?? string.Empty);
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

    private void LogDisconnectedMcpServers(string line)
    {
        foreach (var (name, status) in ParseDisconnectedMcpServers(line))
        {
            _logger.LogWarning("MCP-сервер «{Name}» агенту не подключился (статус «{Status}») – его инструменты будут недоступны", name, status);
        }
    }

    private static async Task DrainStderrAsync(Process process, List<string> tail, string? mcpToken)
    {
        const int maxLines = 20;

        try
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (tail.Count >= maxLines)
                {
                    tail.RemoveAt(0);
                }

                tail.Add(RedactToken(line, mcpToken));
            }
        }
        catch (IOException)
        {
        }
    }

    private static ProcessStartInfo BuildProcessStartInfo(string executablePath, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppStorage.DataDirectory,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    private sealed record McpConfigServer(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("headers")] Dictionary<string, string> Headers);
}
