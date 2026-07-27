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
}
