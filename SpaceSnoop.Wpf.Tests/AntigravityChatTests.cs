using KeepShell.Bootstrap;
using KeepShell.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Wpf.Agent;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.Mcp;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Chat;
using SpaceSnoop.Wpf.ViewModels.Settings;
using SpaceSnoop.Wpf.ViewModels.Scan;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class AntigravityChatTests
{
    private string _directory = null!;
    private KeepShellLogging _logging = null!;
    private ServiceProvider _services = null!;
    private AgentBackends _backends = null!;
    private bool _keepArtifacts;

    [SetUp]
    public void SetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);
        AppThemes.Register();
        var artifacts = TestContext.Parameters.Get("AntigravityArtifactsDirectory", string.Empty);
        _keepArtifacts = TestContext.Parameters.Get("RunLiveAntigravity", false) && !string.IsNullOrWhiteSpace(artifacts);
        _directory = _keepArtifacts
            ? Directory.CreateDirectory(Path.Combine(Path.GetFullPath(artifacts), Guid.NewGuid().ToString("N"))).FullName
            : Directory.CreateTempSubdirectory("spacesnoop-antigravity-chat-").FullName;

        ISettingsStore settings = new MemorySettings();
        settings.SetEnum(SettingsKeys.AgentBackend, AgentBackendKind.Antigravity);
        settings.SetBool(SettingsKeys.AgentConsent(AgentBackendKind.Antigravity), true);
        settings.SetBool(SettingsKeys.AgentTranscript, true);
        settings.SetBool(SettingsKeys.McpEnabled, true);
        settings.SetBool(SettingsKeys.McpAllowMutations, false);
        settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        settings.SetInt(SettingsKeys.McpPort, ((IPEndPoint)listener.LocalEndpoint).Port);

        _logging = KeepShellLogging.Bootstrap(new() { LogsDirectory = Path.Combine(_directory, "logs"), FileNamePrefix = "agent-test" });
        _services = App.ConfigureServices(settings, _logging);
        _backends = new(_services.GetRequiredService<AgentPreferences>(), Path.Combine(_directory, "agents"), _logging.LoggerFactory);
    }

    [TearDown]
    public void TearDown()
    {
        _backends?.Dispose();
        _services?.Dispose();
        _logging?.Dispose();
        if (!_keepArtifacts && Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Test]
    public void Чат_передаёт_MCP_политику_и_сохраняет_финальный_ответ_пакета()
    {
        var preferences = _services.GetRequiredService<AgentPreferences>();
        preferences.CliPath = WriteFakeCli();
        var configDirectory = Directory.CreateDirectory(Path.Combine(_directory, "agents", "antigravity", ".agents"));
        File.WriteAllText(Path.Combine(configDirectory.FullName, "mcp_config.json"), "осиротевший конфиг");
        var chat = CreateChat();
        VisualTestHost.Run(async () =>
        {
            await ReadyAsync(chat);
            chat.InputText = "Проверь состояние приложения";
            await chat.SendCommand.ExecuteAsync(null);
        });

        var answer = chat.Messages[^1];
        var history = new ChatHistoryStore(NullLogger<ChatHistoryStore>.Instance, Path.Combine(_directory, ChatHistoryStore.FileName)).Load().Single();
        Assert.Multiple(() =>
        {
            Assert.That(answer.IsError, Is.False, answer.Text);
            Assert.That(answer.Text, Is.EqualTo("Готово."));
            Assert.That(answer.ToolCalls.Single().Name, Is.EqualTo("mcp__spacesnoop__get_app_state"));
            Assert.That(answer.CostUsd, Is.Null);
            Assert.That(answer.Tokens, Is.Null);
            Assert.That(history.Backend, Is.EqualTo(AgentBackendKind.Antigravity));
            Assert.That(history.SessionId, Is.EqualTo("fixture-session"));
            Assert.That(history.Messages[^1].Text, Is.EqualTo("Готово."));
            Assert.That(Directory.GetFiles(Path.Combine(_directory, "agents"), "*.json", SearchOption.AllDirectories), Is.Empty);
        });
    }

    [TestCase(AgentBackendKind.Claude)]
    [TestCase(AgentBackendKind.OpenCode)]
    public async Task Предупреждения_пакетного_адаптера_попадают_в_журнал_приложения(AgentBackendKind kind)
    {
        var preferences = _services.GetRequiredService<AgentPreferences>();
        preferences.Backend = kind;
        var backend = _backends.Current;
        var path = Path.Combine(_directory, $"{backend.CliName}.cmd");
        await File.WriteAllTextAsync(path, "@echo off\r\nif \"%~1\"==\"--version\" (echo 1.0-fixture&exit /b 0)\r\nset /p inputLine=\r\necho {}\r\n", new UTF8Encoding(false));
        preferences.CliPath = path;
        backend = _backends.Current;
        _backends.PrepareWorkspace(kind);

        var events = new List<AgentEvent>();
        await foreach (var item in backend.RunAsync(new() { Prompt = "Вопрос", Transcript = new FailingTranscript() }, CancellationToken.None))
        {
            events.Add(item);
        }

        Assert.That(events.Select(item => item.Kind), Does.Contain(AgentEventKind.Failed));
        Assert.That(_logging.Sink.Snapshot().Any(item =>
            item.RenderMessage().Contains("Agent observer failed", StringComparison.Ordinal) &&
            item.Properties["SourceContext"].ToString().Trim('"') == backend.GetType().FullName), Is.True);
    }

    [Test]
    public void Каталог_Antigravity_загружается_адаптером_из_agy_models()
    {
        var preferences = _services.GetRequiredService<AgentPreferences>();
        preferences.CliPath = WriteFakeCli();
        var selector = new AgentModelSelector(preferences, _backends, NullLogger<AgentModelSelector>.Instance);
        VisualTestHost.Run(selector.EnsureModelsAsync);

        Assert.That(selector.ModelOptions.Select(item => item.Id), Does.Contain("fixture-model"));
    }

    [Test]
    public void Живая_модель_Antigravity_вызывает_MCP_SpaceSnoop_и_отвечает_в_чате()
    {
        if (!TestContext.Parameters.Get("RunLiveAntigravity", false))
        {
            Assert.Ignore("Живой вызов включается параметром RunLiveAntigravity=true.");
        }

        var preferences = _services.GetRequiredService<AgentPreferences>();
        preferences.Model = TestContext.Parameters.Get("AntigravityModel", "gemini-3.8-flash-medium");
        var chat = CreateChat();
        VisualTestHost.Run(async () =>
        {
            await ReadyAsync(chat);
            chat.InputText = "Вызови ровно один MCP-инструмент SpaceSnoop get_app_state. Не используй другие инструменты. По его результату напиши название приложения и его версию. Без успешного вызова сообщи об ошибке, не угадывай данные.";
            await chat.SendCommand.ExecuteAsync(null);
        });

        var answer = chat.Messages[^1];
        var token = _services.GetRequiredService<McpPreferences>().Token;
        File.WriteAllText(Path.Combine(_directory, "live-result.json"), JsonSerializer.Serialize(new
        {
            cli = chat.Gates.CliInfo?.Version,
            model = preferences.Model,
            answer.Text,
            answer.IsError,
            answer.IsCancelled,
            tools = answer.ToolCalls.Select(item => new { item.Name, item.Arguments }),
            session = chat.History.SessionId,
            answer.TranscriptPath,
            tokenRedacted = !File.ReadAllText(answer.TranscriptPath!).Contains(token, StringComparison.Ordinal),
        }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        TestContext.Out.WriteLine($"Доказательства живого вызова: {_directory}");

        Assert.Multiple(() =>
        {
            Assert.That(answer.IsError, Is.False, answer.Text);
            Assert.That(answer.IsCancelled, Is.False);
            Assert.That(answer.ToolCalls.Select(item => AgentPrompt.ShortName(item.Name)), Does.Contain("get_app_state"));
            Assert.That(answer.Text, Does.Contain(AppInfo.Name));
            Assert.That(answer.Text, Does.Contain(AppInfo.Version));
            Assert.That(chat.History.SessionId, Is.Not.Null.And.Not.Empty);
            Assert.That(File.ReadAllText(answer.TranscriptPath!), Does.Not.Contain(token));
        });

        var session = chat.History.SessionId;
        VisualTestHost.Run(async () =>
        {
            chat.InputText = "Повтори проверку get_app_state через MCP и назови версию приложения. Другие инструменты не используй.";
            await chat.SendCommand.ExecuteAsync(null);
        });
        var followup = chat.Messages[^1];
        File.WriteAllText(Path.Combine(_directory, "live-followup.json"), JsonSerializer.Serialize(new
        {
            followup.Text,
            followup.IsError,
            followup.IsCancelled,
            tools = followup.ToolCalls.Select(item => item.Name),
            session = chat.History.SessionId,
            previousSession = session,
            followup.TranscriptPath,
        }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        Assert.Multiple(() =>
        {
            Assert.That(followup.IsError, Is.False, followup.Text);
            Assert.That(followup.IsCancelled, Is.False);
            Assert.That(followup.ToolCalls.Select(item => AgentPrompt.ShortName(item.Name)), Does.Contain("get_app_state"));
            Assert.That(followup.Text, Does.Contain(AppInfo.Version));
            Assert.That(chat.History.SessionId, Is.EqualTo(session));
            Assert.That(chat.Messages, Has.Count.EqualTo(4));
        });
    }

    private ChatViewModel CreateChat()
    {
        var preferences = _services.GetRequiredService<AgentPreferences>();
        var selector = new AgentModelSelector(preferences, _backends, NullLogger<AgentModelSelector>.Instance);
        return new(_backends, preferences, _services.GetRequiredService<ScanPreferences>(), selector,
            _services.GetRequiredService<McpPreferences>(), _services.GetRequiredService<McpServerHost>(), _services.GetRequiredService<McpBridge>(),
            new(NullLogger<ChatHistoryStore>.Instance, Path.Combine(_directory, ChatHistoryStore.FileName)),
            new(preferences, NullLogger<AgentTranscriptStore>.Instance, Path.Combine(_directory, "transcripts")),
            new NoopDialogs(), new FakeClipboard(), new FakeShellLauncher(), new FakeUiDispatcher(),
            NullLogger<ChatViewModel>.Instance, new FakeAppNavigator());
    }

    private static async Task ReadyAsync(ChatViewModel chat)
    {
        chat.Gates.McpServer.Apply();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!chat.Gates.McpServer.IsRunning && chat.Gates.McpServer.LastError is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.That(chat.Gates.McpServer.IsRunning, Is.True, chat.Gates.McpServer.LastError);
        await chat.EnsureLoadedAsync();
        Assert.That(chat.Gates.CanChat, Is.True);
    }

    private string WriteFakeCli()
    {
        var path = Path.Combine(_directory, "agy.cmd");
        File.WriteAllText(path, "@echo off\r\nif \"%~1\"==\"--version\" (echo 1.5.5-fixture&exit /b 0)\r\nif \"%~1\"==\"models\" (echo fixture-model\tFixture Model&exit /b 0)\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%~dp0response.ps1\" %*\r\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(_directory, "response.ps1"), """
            [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
            $settingsPath = $args[[Array]::IndexOf($args, '--settings') + 1]
            $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
            $mcp = Get-Content -LiteralPath '.agents/mcp_config.json' -Raw | ConvertFrom-Json
            $inputLine = [Console]::ReadLine() | ConvertFrom-Json
            if ($settings.toolPermission -ne 'request-review' -or
                $settings.permissions.allow -notcontains 'mcp(spacesnoop/get_app_state)' -or
                $settings.permissions.deny -notcontains 'mcp(spacesnoop/cleanup_run)' -or
                $mcp.mcpServers.spacesnoop.serverUrl -notlike 'http://127.0.0.1:*/mcp' -or
                $mcp.mcpServers.spacesnoop.headers.Authorization -notlike 'Bearer *' -or
                $inputLine.event -ne 'user') { exit 17 }
            [Console]::WriteLine('{"event":"init","conversation_id":"fixture-session"}')
            [Console]::WriteLine('{"event":"text_delta","text":"Преамбула"}')
            [Console]::WriteLine('{"event":"step_update","step_update":{"step_type":"tool","tool_name":"mcp__spacesnoop__get_app_state","tool_info":{"parameters":{}}}}')
            [Console]::WriteLine('{"event":"text_delta","text":"Часть ответа"}')
            [Console]::WriteLine('{"event":"result","status":"SUCCESS","conversation_id":"fixture-session","response":"Готово."}')
            """, new UTF8Encoding(true));
        return path;
    }

    private sealed class FailingTranscript : KeepShell.Agents.IAgentTranscript
    {
        public void Write(KeepShell.Agents.AgentTranscriptKind kind, string text) => throw new IOException("Тестовый отказ записи.");
    }
}
