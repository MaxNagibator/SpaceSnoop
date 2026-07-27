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

    [Test]
    public void Статус_хода_называет_последний_инструмент()
    {
        var message = new ChatMessageViewModel(ChatRole.Assistant) { IsStreaming = true };

        message.ToolCalls.Add(ChatToolCall.From("mcp__spacesnoop__get_app_state"));
        message.ToolCalls.Add(ChatToolCall.From("mcp__spacesnoop__scan_directory"));

        Assert.That(message.StatusText, Is.EqualTo(AgentPersona.WorkingOn("сканирование каталога")));
    }
}
