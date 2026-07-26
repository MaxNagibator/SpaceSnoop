using SpaceSnoop.Wpf.Agent;
using SpaceSnoop.Wpf.ViewModels.Chat;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class AgentToolPolicyTests
{
    [Test]
    public void Без_разрешения_мутаций_sync_current_запрещён()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AgentPrompt.AllowedTools(mutations: false), Does.Not.Contain("sync_current"));
            Assert.That(AgentPrompt.DeniedTools(mutations: false), Is.EqualTo(new[] { "sync_current" }));
        }
    }

    [Test]
    public void С_разрешением_мутаций_sync_current_разрешён_и_запретов_нет()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AgentPrompt.AllowedTools(mutations: true), Does.Contain("sync_current"));
            Assert.That(AgentPrompt.DeniedTools(mutations: true), Is.Empty);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Открытие_страницы_синхронизации_разрешено_всегда(bool mutations)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AgentPrompt.AllowedTools(mutations), Does.Contain("open_sync"));
            Assert.That(AgentPrompt.DeniedTools(mutations), Does.Not.Contain("open_sync"));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Читающие_инструменты_разрешены_всегда(bool mutations)
    {
        Assert.That(
            AgentPrompt.AllowedTools(mutations),
            Is.SupersetOf(new[] { "get_app_state", "list_profiles", "scan_directory", "compare_directories", "get_current_comparison" }));
    }

    [TestCase("mcp__spacesnoop__scan_directory", ExpectedResult = "scan_directory")]
    [TestCase("scan_directory", ExpectedResult = "scan_directory")]
    [TestCase("mcp__другой__scan_directory", ExpectedResult = "mcp__другой__scan_directory")]
    [TestCase("", ExpectedResult = "")]
    public string Короткое_имя_отбрасывает_префикс_своего_сервера(string toolName)
    {
        return AgentPrompt.ShortName(toolName);
    }

    [TestCase("mcp__spacesnoop__sync_current", ExpectedResult = true)]
    [TestCase("sync_current", ExpectedResult = true)]
    [TestCase("mcp__spacesnoop__open_sync", ExpectedResult = false)]
    [TestCase("mcp__spacesnoop__scan_directory", ExpectedResult = false)]
    [TestCase("неизвестный", ExpectedResult = false)]
    public bool Разрушающим_считается_только_sync_current(string toolName)
    {
        return AgentPrompt.IsDestructive(toolName);
    }

    [Test]
    public void Незнакомый_инструмент_показывается_коротким_именем()
    {
        Assert.That(AgentPrompt.Describe("mcp__spacesnoop__future_tool"), Is.EqualTo("future_tool"));
    }

    [Test]
    public void Вызов_sync_current_помечается_как_изменяющий()
    {
        var call = ChatToolCall.From("mcp__spacesnoop__sync_current");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(call.IsMutating, Is.True);
            Assert.That(call.Text, Is.EqualTo("синхронизация"));
        }
    }

    [TestCase(null)]
    [TestCase("mcp__spacesnoop__scan_directory")]
    public void Читающий_или_неизвестный_вызов_не_помечается(string? toolName)
    {
        Assert.That(ChatToolCall.From(toolName).IsMutating, Is.False);
    }

    [Test]
    public void Промпт_без_мутаций_объявляет_запрет_на_перенос()
    {
        var prompt = AgentPrompt.Build(mutations: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("Переносить и удалять файлы ты не можешь"));
            Assert.That(prompt, Does.Not.Contain("sync_current"));
        }
    }

    [Test]
    public void Промпт_с_мутациями_требует_сначала_план_и_согласие()
    {
        var prompt = AgentPrompt.Build(mutations: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("dryRun=true"));
            Assert.That(prompt, Does.Contain("dryRun=false"));
            Assert.That(prompt, Does.Contain("явно на него"));
        }
    }

    [Test]
    public void Дефолтный_промпт_равен_read_only_редакции()
    {
        Assert.That(AgentPrompt.System, Is.EqualTo(AgentPrompt.Build(mutations: false)));
    }

    [Test]
    public void Обе_редакции_промпта_несут_общую_часть()
    {
        Assert.That(AgentPrompt.Build(mutations: true), Does.StartWith("Ты – помощник внутри программы SpaceSnoop"));
    }
}
