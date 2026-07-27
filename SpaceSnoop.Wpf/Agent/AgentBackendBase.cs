using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace SpaceSnoop.Wpf.Agent;

public abstract class AgentBackendBase : IAgentBackend, IDisposable
{
    internal const string TokenVariable = "SPACESNOOP_MCP_TOKEN";

    private readonly AgentPreferences _preferences;
    private readonly ILogger _logger;

    private readonly HashSet<Process> _live = [];

    private bool _cacheValid;
    private string _cachedForCliPath = string.Empty;
    private AgentCliInfo? _cached;

    protected AgentBackendBase(AgentPreferences preferences, ILogger logger)
    {
        _preferences = preferences;
        _logger = logger;
    }

    public abstract AgentBackendKind Kind { get; }

    public abstract string DisplayName { get; }

    public abstract string CliName { get; }

    public abstract bool HasBuiltInShell { get; }

    public abstract bool SendsSystemPromptEachTurn { get; }

    public abstract string MissingCliHint { get; }

    public virtual string ModelHint => "Модель, которой нет в списке: слаг уходит в CLI как есть и появляется отдельным пунктом выше. Пусто – модель, выбранная по умолчанию в самом CLI.";

    protected abstract IReadOnlyList<string> ExtraDirectories { get; }

    protected abstract AgentLaunch CreateLaunch(AgentRequest request);

    protected abstract IAgentStreamParser CreateParser();

    protected virtual void InspectLine(string line)
    {
    }

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

        GC.SuppressFinalize(this);
    }

    public virtual IReadOnlyList<AgentModelOption> LoadModels()
    {
        return AgentModels.For(Kind);
    }

    public AgentCliInfo? Detect()
    {
        var overridePath = _preferences.CliPathFor(Kind);

        if (_cacheValid && _cachedForCliPath == overridePath)
        {
            return _cached;
        }

        _cached = AgentCli.Detect(AgentCli.ExecutableNames(CliName), overridePath, ExtraDirectories);
        _cachedForCliPath = overridePath;
        _cacheValid = true;

        if (_cached is not null)
        {
            _logger.AgentCliDetected(_cached.ExecutablePath, _cached.Version);
        }
        else
        {
            _logger.AgentCliMissing(DisplayName);
        }

        return _cached;
    }

    public async IAsyncEnumerable<AgentEvent> RunAsync(AgentRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cli = await Task.Run(Detect, cancellationToken).ConfigureAwait(false);

        if (cli is null)
        {
            var reason = $"CLI {DisplayName} не найден на машине – {MissingCliHint}";
            _logger.AgentTurnFailed(null, reason);
            yield return AgentEvent.Fail(reason);
            yield break;
        }

        var launch = CreateLaunch(request);
        var transcript = request.Transcript;
        Process? process = null;
        var stderrTail = new List<string>();
        var resultYielded = false;
        var stopwatch = Stopwatch.StartNew();
        var parser = CreateParser();

        _logger.AgentTurnStarted(DisplayName, request.Mcp?.AllowedTools.Count ?? 0, !string.IsNullOrEmpty(request.ResumeSessionId));

        try
        {
            transcript?.Write(AgentTranscriptKind.Launch, DescribeLaunch(DisplayName, cli, request, launch));

            foreach (var file in launch.TempFiles)
            {
                await File.WriteAllTextAsync(file.Path, file.Content, cancellationToken).ConfigureAwait(false);
                transcript?.Write(AgentTranscriptKind.Config, $"{file.Path}\n{file.Content}");
            }

            var info = BuildProcessStartInfo(cli.ExecutablePath, launch);

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
                var reason = $"Не удалось запустить CLI {DisplayName}.";
                yield return AgentEvent.Fail(reason);
                yield break;
            }

            lock (_live)
            {
                _live.Add(process);
            }

            using var kill = cancellationToken.Register(() => TryKill(process));

            var mcpToken = request.Mcp?.Token;
            var stderrTask = DrainStderrAsync(process, stderrTail, mcpToken, transcript);

            Exception? stdinFailure = null;

            try
            {
                transcript?.Write(AgentTranscriptKind.Stdin, launch.Stdin);
                await process.StandardInput.WriteAsync(launch.Stdin).ConfigureAwait(false);
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

                var reason = BuildExitReason(process, stderrTail, parser.FailureHint);
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

                transcript?.Write(AgentTranscriptKind.Stdout, line);
                InspectLine(line);

                var agentEvent = parser.Parse(line);

                if (agentEvent is null)
                {
                    continue;
                }

                switch (agentEvent.Kind)
                {
                    case AgentEventKind.ToolCall:
                        _logger.AgentToolInvoked(agentEvent.ToolName ?? string.Empty);
                        break;

                    case AgentEventKind.Completed:
                        resultYielded = true;
                        _logger.AgentTurnCompleted(stopwatch.ElapsedMilliseconds, agentEvent.CostUsd, agentEvent.Tokens);
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

            if (!resultYielded && process.ExitCode == 0 && parser.Complete() is { } completion)
            {
                resultYielded = true;
                _logger.AgentTurnCompleted(stopwatch.ElapsedMilliseconds, completion.CostUsd, completion.Tokens);
                yield return completion;
            }

            if (!resultYielded)
            {
                var reason = BuildExitReason(process, stderrTail, parser.FailureHint);
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
                transcript?.Write(AgentTranscriptKind.Exit, $"код {(process.HasExited ? process.ExitCode : -1)}, {stopwatch.ElapsedMilliseconds} мс, отмена {cancellationToken.IsCancellationRequested}");

                lock (_live)
                {
                    _live.Remove(process);
                }

                TryKill(process);
                process.Dispose();
            }

            foreach (var file in launch.TempFiles)
            {
                TryDelete(file.Path);
            }
        }
    }

    internal static string DescribeLaunch(string backend, AgentCliInfo cli, AgentRequest request, AgentLaunch launch)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"бэкенд: {backend}");
        builder.AppendLine($"CLI: {cli.ExecutablePath} ({cli.Version})");
        builder.AppendLine($"модель: {Named(request.Model)}");
        builder.AppendLine($"рассуждения: {Named(request.Effort)}");
        builder.AppendLine($"продолжение сессии: {Named(request.ResumeSessionId)}");
        builder.AppendLine($"инструменты: {string.Join(", ", request.Mcp?.AllowedTools ?? [])}");
        builder.AppendLine($"переменные окружения: {string.Join(", ", launch.Environment.Keys)}");
        builder.Append($"аргументы: {string.Join(' ', launch.Arguments)}");

        return builder.ToString();
    }

    internal static string RedactToken(string text, string? token)
    {
        return string.IsNullOrEmpty(token) ? text : text.Replace(token, "<токен>");
    }

    protected static string TempConfigPath(string extension)
    {
        return Path.Combine(Path.GetTempPath(), $"spacesnoop-mcp-{Guid.NewGuid():N}.{extension}");
    }

    private string BuildExitReason(Process process, IReadOnlyList<string> stderrTail, string? failureHint)
    {
        var exitCode = process.HasExited ? process.ExitCode : -1;

        if (!string.IsNullOrWhiteSpace(failureHint))
        {
            return $"CLI {DisplayName} завершился с кодом {exitCode}: {failureHint}";
        }

        return stderrTail.Count > 0
            ? $"CLI {DisplayName} завершился с кодом {exitCode}: {string.Join(" ", stderrTail)}"
            : $"CLI {DisplayName} завершился с кодом {exitCode} без ответа.";
    }

    private static string Named(string? value)
    {
        return value is { Length: > 0 } ? value : "–";
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

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async Task DrainStderrAsync(Process process, List<string> tail, string? mcpToken, IAgentTranscript? transcript)
    {
        const int maxLines = 20;

        try
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                transcript?.Write(AgentTranscriptKind.Stderr, line);

                if (tail.Count >= maxLines)
                {
                    tail.RemoveAt(0);
                }

                tail.Add(RedactToken(line, mcpToken));
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private static ProcessStartInfo BuildProcessStartInfo(string executablePath, AgentLaunch launch)
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

        foreach (var argument in launch.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in launch.Environment)
        {
            info.Environment[name] = value;
        }

        return info;
    }
}
