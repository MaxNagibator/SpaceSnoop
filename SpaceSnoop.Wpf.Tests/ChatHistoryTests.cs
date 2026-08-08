using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Wpf.Agent;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Chat;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ChatHistoryTests
{
    private string _path = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _path = Path.Combine(Path.GetTempPath(), $"spacesnoop-chat-{Guid.NewGuid():n}.json");
    }

    [TearDown]
    public void TearDown()
    {
        File.Delete(_path);
    }

    [TestCase("", ExpectedResult = ChatHistoryStore.UntitledConversation)]
    [TestCase("   \r\n   ", ExpectedResult = ChatHistoryStore.UntitledConversation)]
    [TestCase("Куда делось место на диске C?", ExpectedResult = "Куда делось место на диске C?")]
    [TestCase("Первая строка\nвторая строка", ExpectedResult = "Первая строка")]
    public string Заголовок_разговора_берётся_из_первой_строки_вопроса(string prompt)
    {
        return ChatHistoryStore.MakeTitle(prompt);
    }

    [Test]
    public void Длинный_вопрос_обрезается_по_границе_слова()
    {
        const string Prompt = "Посмотри, что занимает место в каталоге сборок, и скажи, что оттуда можно снести";

        var title = ChatHistoryStore.MakeTitle(Prompt);
        var head = title.TrimEnd('…');

        using (Assert.EnterMultipleScope())
        {
            Assert.That(title, Has.Length.LessThanOrEqualTo(ChatHistoryStore.TitleLength + 1));
            Assert.That(title, Does.EndWith("…"));
            Assert.That(head, Is.EqualTo(Prompt[..head.Length]));
            Assert.That(Prompt[head.Length], Is.EqualTo(' '));
        }
    }

    [Test]
    public void История_режется_до_потолка_а_пустые_разговоры_не_хранятся()
    {
        List<ChatConversationRecord> conversations =
        [
            Conversation("свежий", "Куда делось место?"),
            .. Enumerable.Range(0, AppDefaults.AgentHistoryLimit + 5).Select(index => Conversation($"старый-{index}", "Вопрос")),
            new() { Id = "пустой" },
        ];

        var trimmed = ChatHistoryStore.Trim(conversations);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(trimmed, Has.Count.EqualTo(AppDefaults.AgentHistoryLimit));
            Assert.That(trimmed[0].Id, Is.EqualTo("свежий"));
            Assert.That(trimmed.Exists(conversation => conversation.Id == "пустой"), Is.False);
        }
    }

    [Test]
    public void Разговор_переживает_запись_и_чтение()
    {
        var store = new ChatHistoryStore(NullLogger<ChatHistoryStore>.Instance, _path);
        var saved = Conversation("разговор", "Куда делось место?") with
        {
            Backend = AgentBackendKind.Codex,
            SessionId = "session-42",
        };

        store.Save([saved]);
        var loaded = store.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0].Title, Is.EqualTo("Куда делось место?"));
            Assert.That(loaded[0].Backend, Is.EqualTo(AgentBackendKind.Codex));
            Assert.That(loaded[0].SessionId, Is.EqualTo("session-42"));
            Assert.That(loaded[0].Messages, Has.Count.EqualTo(2));
            Assert.That(loaded[0].Messages[1].Tools, Is.EqualTo(new[] { "scan_directory" }));
        }
    }

    [Test]
    public void Битая_история_не_роняет_чат()
    {
        File.WriteAllText(_path, "{ не json");

        var store = new ChatHistoryStore(NullLogger<ChatHistoryStore>.Instance, _path);

        Assert.That(store.Load(), Is.Empty);
    }

    [Test]
    public void Сообщение_восстанавливается_с_бейджами_инструментов_и_расходом()
    {
        var source = new ChatMessageViewModel(ChatRole.Assistant, "Нашёл виновника") { CostUsd = 0.0123, Tokens = 4200 };
        source.ToolCalls.Add(ChatToolCall.From("scan_directory"));
        source.ToolCalls.Add(ChatToolCall.From("sync_current"));

        var restored = ChatMessageViewModel.Restore(source.ToRecord());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.Text, Is.EqualTo("Нашёл виновника"));
            Assert.That(restored.CostUsd, Is.EqualTo(0.0123));
            Assert.That(restored.Tokens, Is.EqualTo(4200));
            Assert.That(restored.ToolCalls[0].Text, Is.EqualTo(AgentPrompt.Describe("scan_directory")));
            Assert.That(restored.ToolCalls[0].IsMutating, Is.False);
            Assert.That(restored.ToolCalls[1].IsMutating, Is.True);
        }
    }

    private static ChatConversationRecord Conversation(string id, string prompt)
    {
        return new()
        {
            Id = id,
            Title = ChatHistoryStore.MakeTitle(prompt),
            StartedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Messages =
            [
                new() { Role = ChatRole.User, Text = prompt },
                new() { Role = ChatRole.Assistant, Text = "Смотрю", Tools = ["scan_directory"] },
            ],
        };
    }
}
