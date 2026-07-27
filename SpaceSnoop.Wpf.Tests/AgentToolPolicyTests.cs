using ModelContextProtocol.Server;
using SpaceSnoop.Wpf.Agent;
using SpaceSnoop.Wpf.Mcp;
using SpaceSnoop.Wpf.ViewModels.Chat;
using System.Reflection;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class AgentToolPolicyTests
{
    private static readonly string[] MutatingTools = ["sync_current", "archive_directory", "mark_for_deletion"];

    [Test]
    public void Без_разрешения_мутаций_изменяющие_инструменты_запрещены()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AgentPrompt.AllowedTools(mutations: false).Intersect(MutatingTools), Is.Empty);
            Assert.That(AgentPrompt.DeniedTools(mutations: false), Is.EquivalentTo(MutatingTools));
        }
    }

    [Test]
    public void С_разрешением_мутаций_изменяющие_инструменты_разрешены_и_запретов_нет()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AgentPrompt.AllowedTools(mutations: true), Is.SupersetOf(MutatingTools));
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
            Is.SupersetOf(new[]
            {
                "get_app_state",
                "list_profiles",
                "list_drives",
                "scan_directory",
                "get_current_scan",
                "compare_directories",
                "get_current_comparison",
                "docker_usage",
                "open_scan",
            }));
    }

    [Test]
    public void Каждый_инструмент_сервера_объявлен_в_политике_и_назван_по_русски()
    {
        var tools = typeof(SpaceSnoopTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(static method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .OfType<string>()
            .ToList();

        Assert.That(tools, Is.Not.Empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(AgentPrompt.AllowedTools(mutations: true), Is.EquivalentTo(tools));

            foreach (var tool in tools)
            {
                Assert.That(AgentPrompt.Describe(tool), Is.Not.EqualTo(tool), $"инструмент {tool} остался без русского имени");
            }
        }
    }

    [TestCase("mcp__spacesnoop__scan_directory", ExpectedResult = "scan_directory")]
    [TestCase("spacesnoop_scan_directory", ExpectedResult = "scan_directory")]
    [TestCase("scan_directory", ExpectedResult = "scan_directory")]
    [TestCase("mcp__другой__scan_directory", ExpectedResult = "mcp__другой__scan_directory")]
    [TestCase("", ExpectedResult = "")]
    public string Короткое_имя_отбрасывает_префикс_своего_сервера(string toolName)
    {
        return AgentPrompt.ShortName(toolName);
    }

    [TestCase("mcp__spacesnoop__sync_current", ExpectedResult = true)]
    [TestCase("spacesnoop_sync_current", ExpectedResult = true)]
    [TestCase("sync_current", ExpectedResult = true)]
    [TestCase("mcp__spacesnoop__archive_directory", ExpectedResult = true)]
    [TestCase("mcp__spacesnoop__mark_for_deletion", ExpectedResult = true)]
    [TestCase("mcp__spacesnoop__open_sync", ExpectedResult = false)]
    [TestCase("mcp__spacesnoop__scan_directory", ExpectedResult = false)]
    [TestCase("mcp__spacesnoop__get_current_scan", ExpectedResult = false)]
    [TestCase("неизвестный", ExpectedResult = false)]
    public bool Разрушающими_считаются_только_объявленные_инструменты(string toolName)
    {
        return AgentPrompt.IsDestructive(toolName);
    }

    [Test]
    public void Незнакомый_инструмент_показывается_коротким_именем()
    {
        Assert.That(AgentPrompt.Describe("mcp__spacesnoop__future_tool"), Is.EqualTo("future_tool"));
    }

    [TestCase("mcp__spacesnoop__sync_current", "синхронизация")]
    [TestCase("mcp__spacesnoop__archive_directory", "упаковка в архив")]
    [TestCase("mcp__spacesnoop__mark_for_deletion", "пометка на удаление")]
    public void Изменяющий_вызов_помечается_и_подписывается_по_русски(string toolName, string caption)
    {
        var call = ChatToolCall.From(toolName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(call.IsMutating, Is.True);
            Assert.That(call.Text, Is.EqualTo(caption));
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
        var prompt = AgentPrompt.Build(mutations: false, shell: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("Переносить и удалять файлы ты не можешь"));

            foreach (var tool in MutatingTools)
            {
                Assert.That(prompt, Does.Not.Contain(tool));
            }
        }
    }

    [Test]
    public void Промпт_с_мутациями_требует_сначала_план_и_согласие()
    {
        var prompt = AgentPrompt.Build(mutations: true, shell: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("dryRun=true"));
            Assert.That(prompt, Does.Contain("dryRun=false"));
            Assert.That(prompt, Does.Contain("явно на него"));

            foreach (var tool in MutatingTools)
            {
                Assert.That(prompt, Does.Contain(tool));
            }
        }
    }

    [Test]
    public void Промпт_без_оболочки_объявляет_что_команд_нет()
    {
        var prompt = AgentPrompt.Build(mutations: false, shell: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("команд не запускаешь"));
            Assert.That(prompt, Does.Not.Contain("оболочка операционной системы"));
        }
    }

    [Test]
    public void Промпт_с_оболочкой_запрещает_менять_ею_диск()
    {
        var prompt = AgentPrompt.Build(mutations: false, shell: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.Contain("оболочка операционной системы"));
            Assert.That(prompt, Does.Contain("запрещены"));
            Assert.That(prompt, Does.Not.Contain("команд не запускаешь"));
        }
    }

    [Test]
    public void Дефолтный_промпт_равен_read_only_редакции_без_оболочки()
    {
        Assert.That(AgentPrompt.System, Is.EqualTo(AgentPrompt.Build(mutations: false, shell: false)));
    }

    [Test]
    public void Обе_редакции_промпта_несут_общую_часть()
    {
        Assert.That(AgentPrompt.Build(mutations: true, shell: false), Does.StartWith($"Тебя зовут {AgentPersona.Name}"));
    }

    [Test]
    public void Запуск_команды_помечается_как_изменяющий_вызов()
    {
        Assert.That(ChatToolCall.From(AgentPrompt.ShellTool).IsMutating, Is.True);
    }
}
