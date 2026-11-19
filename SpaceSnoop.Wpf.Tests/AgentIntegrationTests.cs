using KeepShell.Bootstrap;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Wpf.Agent;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Chat;
using SpaceSnoop.Wpf.ViewModels.Settings;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class AgentIntegrationTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("spacesnoop-agent-integration-").FullName;
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, true);
    }

    [Test]
    public void Реестр_предлагает_поддерживаемых_агентов_без_создания_рабочих_каталогов()
    {
        using var registry = new AgentBackends(new(new MemorySettings()), _directory);

        Assert.Multiple(() =>
        {
            Assert.That(registry.All.Select(item => item.Id), Is.EqualTo(new[] { "claude-code", "codex", "opencode", "antigravity" }));
            Assert.That(registry.All.All(item => !item.HasBuiltInShell), Is.True);
            Assert.That(Directory.GetFileSystemEntries(_directory), Is.Empty);
        });
    }

    [Test]
    public void После_смены_пути_обнаружение_использует_новый_CLI()
    {
        var first = WriteCli("first", "1.2.3");
        var second = WriteCli("second", "4.5.6");
        var preferences = new AgentPreferences(new MemorySettings())
        {
            Backend = AgentBackendKind.Antigravity,
            CliPath = first,
        };
        using var registry = new AgentBackends(preferences, _directory);

        using (Assert.EnterMultipleScope())
        {
            var detected = registry.Current.Detect();
            Assert.That(detected?.ExecutablePath, Is.EqualTo(first));
            Assert.That(detected?.Version, Is.EqualTo("1.2.3"));
        }

        preferences.CliPath = second;

        using (Assert.EnterMultipleScope())
        {
            var detected = registry.Current.Detect();
            Assert.That(detected?.ExecutablePath, Is.EqualTo(second));
            Assert.That(detected?.Version, Is.EqualTo("4.5.6"));
        }
    }

    private string WriteCli(string name, string version)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_directory, name));
        var path = Path.Combine(directory.FullName, "agy.cmd");
        File.WriteAllText(path, $"@echo off\r\necho {version}\r\n");
        return path;
    }

    [TestCase(AgentBackendKind.Claude)]
    [TestCase(AgentBackendKind.Codex)]
    [TestCase(AgentBackendKind.OpenCode)]
    [TestCase(AgentBackendKind.Antigravity)]
    public void Настройки_четырёх_агентов_переживают_перезапуск_раздельно(AgentBackendKind selected)
    {
        var path = Path.Combine(_directory, "settings.toml");
        var efforts = new Dictionary<AgentBackendKind, string>
        {
            [AgentBackendKind.Claude] = "xhigh",
            [AgentBackendKind.Codex] = "ultra",
            [AgentBackendKind.OpenCode] = "medium",
            [AgentBackendKind.Antigravity] = "high",
        };
        using (var settings = new SettingsStore(path))
        {
            var preferences = new AgentPreferences(settings);
            foreach (var (kind, effort) in efforts)
            {
                preferences.Backend = kind;
                preferences.Model = $"model-{kind}";
                preferences.Effort = effort;
                preferences.CliPath = $@"C:\cli\{kind}.exe";
                preferences.Consent = kind == selected;
            }

            preferences.Backend = selected;
            settings.Flush();
        }

        using var reopened = new SettingsStore(path);
        var restored = new AgentPreferences(reopened);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Backend, Is.EqualTo(selected));
            Assert.That(restored.Model, Is.EqualTo($"model-{selected}"));
            Assert.That(restored.Effort, Is.EqualTo(efforts[selected]));
            Assert.That(restored.CliPath, Is.EqualTo($@"C:\cli\{selected}.exe"));
            Assert.That(restored.Consent, Is.True);
            foreach (var (kind, effort) in efforts)
            {
                Assert.That(restored.ModelFor(kind), Is.EqualTo($"model-{kind}"));
                Assert.That(restored.EffortFor(kind), Is.EqualTo(effort));
                Assert.That(restored.CliPathFor(kind), Is.EqualTo($@"C:\cli\{kind}.exe"));
                Assert.That(restored.ConsentFor(kind), Is.EqualTo(kind == selected));
            }
        });
    }

    [TestCase("Claude", AgentBackendKind.Claude)]
    [TestCase("Codex", AgentBackendKind.Codex)]
    [TestCase("OpenCode", AgentBackendKind.OpenCode)]
    public void История_до_миграции_сохраняет_агента_сессию_и_стоимость(string name, AgentBackendKind expected)
    {
        var path = Path.Combine(_directory, ChatHistoryStore.FileName);
        File.WriteAllText(path, $$"""
            {"version":1,"conversations":[{"id":"old","backend":"{{name}}","sessionId":"old-session","messages":[{"role":"Assistant","text":"Ответ","costUsd":0.25,"tokens":12,"tools":["get_app_state"]}]}]}
            """);
        var store = new ChatHistoryStore(NullLogger<ChatHistoryStore>.Instance, path);
        var record = store.Load().Single();
        store.Save([record]);
        var reopened = store.Load().Single();

        Assert.Multiple(() =>
        {
            Assert.That(reopened.Backend, Is.EqualTo(expected));
            Assert.That(reopened.SessionId, Is.EqualTo("old-session"));
            Assert.That(reopened.Messages.Single().CostUsd, Is.EqualTo(0.25));
            Assert.That(reopened.Messages.Single().Tokens, Is.EqualTo(12));
            Assert.That(reopened.Messages.Single().Tools, Is.EqualTo(new[] { "get_app_state" }));
        });
    }

    [Test]
    public void Неизвестный_расход_остаётся_неизвестным_в_истории()
    {
        var message = new ChatMessageViewModel(ChatRole.Assistant, "Ответ");
        var restored = ChatMessageViewModel.Restore(message.ToRecord());
        Assert.Multiple(() =>
        {
            Assert.That(restored.CostUsd, Is.Null);
            Assert.That(restored.Tokens, Is.Null);
            Assert.That(restored.HasUsage, Is.False);
        });
    }

    [Test]
    public void Рабочий_каталог_изолирован_от_проекта_и_доступен_только_пользователю()
    {
        using var registry = new AgentBackends(new(new MemorySettings()), _directory);
        registry.PrepareWorkspace(AgentBackendKind.Antigravity);
        var directory = new DirectoryInfo(Path.Combine(_directory, "antigravity"));
        var security = directory.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(security.AreAccessRulesProtected, Is.True);
            Assert.That(rules, Has.Length.EqualTo(1));
            Assert.That(rules[0].IdentityReference, Is.EqualTo(identity.User));
            Assert.That(rules[0].FileSystemRights, Is.EqualTo(FileSystemRights.FullControl));
        });
    }

    [TestCase(AgentBackendKind.None)]
    [TestCase((AgentBackendKind)99)]
    public void Неизвестный_агент_готовит_каталог_агента_по_умолчанию(AgentBackendKind kind)
    {
        using var registry = new AgentBackends(new(new MemorySettings()), _directory);

        registry.PrepareWorkspace(kind);

        Assert.That(Directory.GetDirectories(_directory).Select(Path.GetFileName),
            Is.EqualTo(new[] { AppDefaults.AgentBackendDefault.ToString().ToLowerInvariant() }));
    }

    [Test]
    public void Осиротевший_MCP_конфиг_удаляется_перед_следующим_ходом_Antigravity()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_directory, "antigravity", ".agents"));
        var config = Path.Combine(directory.FullName, "mcp_config.json");
        var other = Path.Combine(directory.FullName, "other.json");
        File.WriteAllText(config, "старый токен");
        File.WriteAllText(other, "сохранить");
        using var registry = new AgentBackends(new(new MemorySettings()), _directory);

        registry.PrepareWorkspace(AgentBackendKind.Antigravity);
        using (var stream = File.Open(config, FileMode.CreateNew, FileAccess.Write))
        {
            stream.WriteByte(1);
        }

        registry.PrepareWorkspace(AgentBackendKind.Antigravity);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(config), Is.False);
            Assert.That(File.ReadAllText(other), Is.EqualTo("сохранить"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Ссылка_в_рабочем_каталоге_отклоняется_без_изменения_целевого_конфига(bool nested)
    {
        var target = Directory.CreateDirectory(Path.Combine(_directory, "target"));
        var config = Path.Combine(target.FullName, "mcp_config.json");
        File.WriteAllText(config, "не менять");
        var agent = Path.Combine(_directory, "antigravity");
        var link = nested ? Path.Combine(Directory.CreateDirectory(agent).FullName, ".agents") : agent;
        try
        {
            Directory.CreateSymbolicLink(link, target.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore("Не удалось создать символьную ссылку.");
        }
        try
        {
            using var registry = new AgentBackends(new(new MemorySettings()), _directory);

            Assert.Throws<IOException>(() => registry.PrepareWorkspace(AgentBackendKind.Antigravity));
            Assert.That(File.ReadAllText(config), Is.EqualTo("не менять"));
        }
        finally
        {
            Directory.Delete(link);
        }
    }
}
