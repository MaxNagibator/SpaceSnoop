using KeepShell.Bootstrap;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Wpf.Agent;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Chat;
using SpaceSnoop.Wpf.ViewModels.Settings;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ChatSessionResetTests
{
    private string _directory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"spacesnoop-chat-reset-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException exception)
        {
            TestContext.Out.WriteLine($"Не удалось удалить каталог {_directory}: {exception.Message}");
        }
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

    [Test]
    public void Сброс_сессии_посреди_хода_помечает_её_брошенной()
    {
        var history = History();

        history.DropSession(busy: true);

        Assert.That(history.SessionDropped, Is.True);
    }

    private ChatHistoryViewModel History()
    {
        var settings = new SettingsStore(Path.Combine(_directory, TomlSettingsFile.PrimaryFileName));

        var preferences = new AgentPreferences(settings);

        var backends = new AgentBackends(
            preferences,
            new ClaudeAgentBackend(preferences, NullLogger<ClaudeAgentBackend>.Instance),
            new CodexAgentBackend(preferences, NullLogger<CodexAgentBackend>.Instance),
            new OpenCodeAgentBackend(preferences, NullLogger<OpenCodeAgentBackend>.Instance));

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
                Messages = [new() { Role = ChatRole.User, Text = "Куда делось место?" }],
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
            () => { },
            () => { });
    }
}
