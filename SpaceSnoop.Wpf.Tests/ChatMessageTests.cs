using SpaceSnoop.Wpf.Agent;
using SpaceSnoop.Wpf.ViewModels.Chat;

namespace SpaceSnoop.Wpf.Tests;

public class ChatMessageTests
{
    [Test]
    public void Сказанное_до_вызова_инструмента_не_попадает_в_ответ()
    {
        var message = new ChatMessageViewModel(ChatRole.Assistant);

        message.Append("I'll scan the main drive.");
        message.DropPreamble();
        message.Append("Нашёл главного: раздутый диск Docker.");

        Assert.That(message.Text, Is.EqualTo("Нашёл главного: раздутый диск Docker."));
    }

    [Test]
    public void Отброшенная_преамбула_гасит_пузырь_до_первых_слов_ответа()
    {
        var message = new ChatMessageViewModel(ChatRole.Assistant);

        message.Append("Let me check the disk.");
        message.DropPreamble();

        Assert.That(message.HasText, Is.False);
    }

    [TestCase("Нашёл виновника: node_modules на 12 ГБ. Снести можно всё")]
    [TestCase("Диск забит. Виновник – Docker, 40 ГБ. Вернуть можно 31,")]
    [TestCase("Индексы Elasticsearch удалять только если данные можно пересоздать. ડ")]
    public void Ответ_без_конца_предложения_считается_оборванным(string text)
    {
        Assert.That(ChatAnswer.LooksTruncated(text), Is.True);
    }

    [TestCase("Нашёл виновника: node_modules на 12 ГБ.")]
    [TestCase("Куда смотреть дальше?")]
    [TestCase("Виновник – `C:\\Temp`, 4 ГБ, чистится целиком.\n")]
    [TestCase("Диск в порядке")]
    [TestCase("")]
    [TestCase("   ")]
    public void Законченный_ответ_оборванным_не_считается(string text)
    {
        Assert.That(ChatAnswer.LooksTruncated(text), Is.False);
    }

    [Test]
    public void Переспросить_можно_только_последний_ответ()
    {
        var message = new ChatMessageViewModel(ChatRole.Assistant, "Нашёл.");

        Assert.That(message.CanRetry, Is.False);

        message.IsLast = true;

        Assert.Multiple(() =>
        {
            Assert.That(message.CanRetry, Is.True);
            Assert.That(message.CanRewind, Is.False);
        });
    }

    [Test]
    public void Пока_ход_идёт_переспрашивать_нечего()
    {
        var message = new ChatMessageViewModel(ChatRole.Assistant) { IsLast = true, IsStreaming = true };

        Assert.That(message.CanRetry, Is.False);
    }

    [Test]
    public void Откатиться_можно_к_вопросу_человека()
    {
        var message = new ChatMessageViewModel(ChatRole.User, "Куда делось место?");

        Assert.Multiple(() =>
        {
            Assert.That(message.CanRewind, Is.True);
            Assert.That(message.CanRetry, Is.False);
            Assert.That(message.LooksTruncated, Is.False);
        });
    }

    [Test]
    public void Статус_хода_называет_последний_инструмент()
    {
        var message = new ChatMessageViewModel(ChatRole.Assistant) { IsStreaming = true };

        message.ToolCalls.Add(ChatToolCall.From("mcp__spacesnoop__get_app_state"));
        message.ToolCalls.Add(ChatToolCall.From("mcp__spacesnoop__scan_directory"));

        Assert.That(message.StatusText, Is.EqualTo(AgentPersona.WorkingOn("сканирование каталога")));
    }

    [Test]
    public void Подряд_идущие_вызовы_одного_инструмента_схлопываются_в_бейдж_со_счётчиком()
    {
        var message = new ChatMessageViewModel(ChatRole.Assistant);

        message.ToolCalls.Add(ChatToolCall.From("mcp__spacesnoop__scan_directory", """{"path":"C:\\Data"}"""));
        message.ToolCalls.Add(ChatToolCall.From("mcp__spacesnoop__scan_directory", """{"path":"C:\\Data\\ВКР"}"""));
        message.ToolCalls.Add(ChatToolCall.From("mcp__spacesnoop__get_app_state", "{}"));
        message.ToolCalls.Add(ChatToolCall.From("mcp__spacesnoop__scan_directory", """{"path":"C:\\Temp"}"""));

        Assert.Multiple(() =>
        {
            Assert.That(message.ToolBadges.Select(badge => badge.Label), Is.EqualTo(new[]
            {
                "сканирование каталога ×2",
                "состояние программы",
                "сканирование каталога",
            }));
            Assert.That(message.ToolBadges[0].Hint, Is.EqualTo($"path: C:\\Data{Environment.NewLine}path: C:\\Data\\ВКР"));
        });
    }

    [Test]
    public void Вызов_из_события_хода_несёт_аргументы()
    {
        var call = ChatToolCall.From(AgentEvent.Tool("mcp__spacesnoop__scan_directory", """{"path":"C:\\Data"}"""));

        Assert.Multiple(() =>
        {
            Assert.That(call.Name, Is.EqualTo("mcp__spacesnoop__scan_directory"));
            Assert.That(call.Arguments, Is.EqualTo("""{"path":"C:\\Data"}"""));
            Assert.That(call.Details, Is.EqualTo("path: C:\\Data"));
        });
    }

    [Test]
    public void Бейдж_без_аргументов_подсказывает_собственным_названием()
    {
        var message = new ChatMessageViewModel(ChatRole.Assistant);

        message.ToolCalls.Add(ChatToolCall.From("mcp__spacesnoop__get_app_state", "{}"));

        Assert.That(message.ToolBadges[0].Hint, Is.EqualTo("состояние программы"));
    }

    [Test]
    public void Бейджи_восстанавливаются_из_истории_вместе_с_аргументами()
    {
        var record = new ChatMessageRecord
        {
            Role = ChatRole.Assistant,
            Text = "Нашёл.",
            Tools = ["mcp__spacesnoop__scan_directory", "mcp__spacesnoop__scan_directory"],
            ToolArguments = ["""{"path":"C:\\Data"}""", string.Empty],
        };

        var message = ChatMessageViewModel.Restore(record);

        Assert.Multiple(() =>
        {
            Assert.That(message.ToolBadges, Has.Count.EqualTo(1));
            Assert.That(message.ToolBadges[0].Label, Is.EqualTo("сканирование каталога ×2"));
            Assert.That(message.ToolBadges[0].Hint, Is.EqualTo("path: C:\\Data"));
        });
    }

    [TestCase("", "")]
    [TestCase("{}", "")]
    [TestCase("""{"path":"C:\\Data","depth":3,"entryLimit":20}""", "path: C:\\Data, depth: 3, entryLimit: 20")]
    [TestCase("""{"mark":true,"paths":["C:\\Data"]}""", "mark: true, paths: [\"C:\\\\Data\"]")]
    [TestCase("""{"left":null}""", "left: –")]
    [TestCase("dir C:\\Data\n", "dir C:\\Data")]
    [TestCase("{сломанный json", "{сломанный json")]
    public void Аргументы_вызова_читаются_парами_ключ_значение(string arguments, string expected)
    {
        Assert.That(ChatToolArguments.Describe(arguments), Is.EqualTo(expected));
    }

    [Test]
    public void Длинное_значение_аргумента_обрезается()
    {
        var path = new string('д', ChatToolArguments.MaxValueLength + 20);

        var text = ChatToolArguments.Describe($$"""{"path":"{{path}}"}""");

        Assert.That(text, Is.EqualTo($"path: {new string('д', ChatToolArguments.MaxValueLength)}…"));
    }
}
