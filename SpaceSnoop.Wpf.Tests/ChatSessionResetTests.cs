using KeepShell.Bootstrap;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Wpf.Agent;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.Mcp;
using SpaceSnoop.Wpf.ViewModels.Chat;
using SpaceSnoop.Wpf.ViewModels.Settings;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ChatSessionResetTests
{
    private string _directory = string.Empty;
    private McpServerHost? _mcpServer;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"spacesnoop-chat-reset-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        _mcpServer?.Dispose();
        _mcpServer = null;

        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException exception)
        {
            TestContext.Out.WriteLine($"Не удалось удалить каталог {_directory}: {exception.Message}");
        }
    }

    [Test]
    public void Переключение_мутаций_посреди_хода_бросает_сессию_CLI_без_системного_промпта_на_каждом_ходе()
    {
        var history = History(out var settings, out var preferences, out var backends);
        preferences.Backend = AgentBackendKind.Codex;
        history.SessionId = "сессия-codex";

        var gates = Gates(
            settings,
            preferences,
            backends,
            history,
            () =>
            {
            });

        gates.Mcp.AllowMutations = !gates.Mcp.AllowMutations;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backends.Current.SendsSystemPromptEachTurn, Is.False);
            Assert.That(history.SessionId, Is.Null);
            Assert.That(history.SessionDropped, Is.True);
        }
    }

    [Test]
    public void Снятие_доступа_останавливает_идущий_ход()
    {
        var history = History(out var settings, out var preferences, out var backends);
        preferences.Enabled = true;
        preferences.Consent = true;
        var cancellations = 0;
        _ = Gates(settings, preferences, backends, history, () => cancellations++);

        preferences.Transcript = !preferences.Transcript;
        Assert.That(cancellations, Is.Zero);

        preferences.Consent = false;
        Assert.That(cancellations, Is.EqualTo(1));

        preferences.Consent = true;
        Assert.That(cancellations, Is.EqualTo(1));

        preferences.Enabled = false;
        Assert.That(cancellations, Is.EqualTo(2));

        preferences.Enabled = true;
        Assert.That(cancellations, Is.EqualTo(2));
    }

    [Test]
    public void История_с_None_возобновляет_сессию_только_в_Claude()
    {
        var history = History(out _, out var preferences, out _);
        File.WriteAllText(Path.Combine(_directory, ChatHistoryStore.FileName), """
            {"version":1,"conversations":[{"id":"old","backend":"None","sessionId":"old-session","messages":[{"role":"User","text":"Вопрос"}]}]}
            """);
        history.LoadHistory();
        history.SelectConversationCommand.Execute(history.Conversations[0]);
        Assert.That(history.SessionId, Is.EqualTo("old-session"));

        history.NewConversationCommand.Execute(null);
        preferences.Backend = AgentBackendKind.Codex;
        history.SelectConversationCommand.Execute(history.Conversations[0]);

        Assert.That(history.SessionId, Is.Null);
    }

    [Test]
    public void Сброс_сессии_снимает_признак_восстановленного_разговора()
    {
        var history = History();

        history.LoadHistory();
        history.SelectConversationCommand.Execute(history.Conversations[0]);

        Assert.That(history.ResumedFromDisk, Is.True);

        history.DropSession(busy: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(history.SessionId, Is.Null);
            Assert.That(history.ResumedFromDisk, Is.False);
            Assert.That(history.SessionDropped, Is.False);
        }
    }

    [TestCase(true, TestName = "Смена CLI из композера посреди хода останавливает ход и бросает сессию прежнего CLI")]
    [TestCase(false, TestName = "Смена CLI из настроек посреди хода останавливает ход и бросает сессию прежнего CLI")]
    public void Смена_CLI_посреди_хода_бросает_сессию(bool fromComposer)
    {
        var history = History(out var settings, out var preferences, out var backends);
        var cancelled = false;
        var gates = Gates(settings, preferences, backends, history, () => cancelled = true);

        history.LoadHistory();
        history.SelectConversationCommand.Execute(history.Conversations[0]);

        var other = gates.BackendOptions.First(option => option.Kind != preferences.Backend);

        if (fromComposer)
        {
            gates.SelectedBackendOption = other;
        }
        else
        {
            preferences.Backend = other.Kind;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preferences.Backend, Is.EqualTo(other.Kind));
            Assert.That(cancelled, Is.True);
            Assert.That(history.SessionId, Is.Null);
            Assert.That(history.ResumedFromDisk, Is.False);
            Assert.That(history.SessionDropped, Is.True);
        }
    }

    private ChatGatesViewModel Gates(SettingsStore settings, AgentPreferences preferences, AgentBackends backends, ChatHistoryViewModel history, Action cancelActiveTurn)
    {
        _mcpServer = new(new(settings), null!, new(settings), null!, NullLogger<McpServerHost>.Instance);

        return new(
            backends,
            preferences,
            new(preferences, backends, NullLogger<AgentModelSelector>.Instance),
            new(settings),
            _mcpServer,
            new FakeUiDispatcher(),
            NullLogger.Instance,
            history,
            () => true,
            cancelActiveTurn);
    }

    private ChatHistoryViewModel History()
    {
        return History(out _, out _, out _);
    }

    private ChatHistoryViewModel History(out SettingsStore settings, out AgentPreferences preferences, out AgentBackends backends)
    {
        settings = new(Path.Combine(_directory, TomlSettingsFile.PrimaryFileName));

        preferences = new(settings);

        backends = new(preferences, Path.Combine(_directory, "agents"));

        var store = new ChatHistoryStore(NullLogger<ChatHistoryStore>.Instance, Path.Combine(_directory, ChatHistoryStore.FileName));

        store.Save(
        [
            new()
            {
                Id = "восстановленный",
                Title = "Куда делось место?",
                Backend = backends.Current.Kind,
                SessionId = "сессия-прежнего-CLI",
                StartedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow,
                Messages =
                [
                    new()
                    {
                        Role = ChatRole.User,
                        Text = "Куда делось место?",
                    },
                ],
            },
        ]);

        return new(
            store,
            new NoopDialogs(),
            NullLogger.Instance,
            backends,
            preferences,
            [],
            () => false,
            () =>
            {
            },
            () =>
            {
            });
    }
}
