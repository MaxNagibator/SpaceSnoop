using KeepShell.Bootstrap;
using SpaceSnoop.Wpf.Agent;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Settings;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class AgentPreferencesTests
{
    private string _directory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"spacesnoop-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public void Прежние_общие_ключи_переносятся_в_ключи_Claude()
    {
        Seed(store =>
        {
            store.SetValue(SettingsKeys.AgentModelShared, "sonnet");
            store.SetValue(SettingsKeys.AgentCliPathShared, @"D:\tools\claude.exe");
        });

        var store = Open();
        var preferences = new AgentPreferences(store);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preferences.ModelFor(AgentBackendKind.Claude), Is.EqualTo("sonnet"));
            Assert.That(preferences.CliPathFor(AgentBackendKind.Claude), Is.EqualTo(@"D:\tools\claude.exe"));
            Assert.That(preferences.ModelFor(AgentBackendKind.Codex), Is.Empty);
            Assert.That(preferences.CliPathFor(AgentBackendKind.Codex), Is.Empty);
        }
    }

    [Test]
    public void Перенесённые_значения_переживают_перезапуск()
    {
        Seed(store =>
        {
            store.SetValue(SettingsKeys.AgentModelShared, "sonnet");
            store.SetValue(SettingsKeys.AgentCliPathShared, @"D:\tools\claude.exe");
        });

        var first = Open();
        _ = new AgentPreferences(first);
        first.Flush();

        var second = Open();
        var preferences = new AgentPreferences(second);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preferences.ModelFor(AgentBackendKind.Claude), Is.EqualTo("sonnet"));
            Assert.That(preferences.CliPathFor(AgentBackendKind.Claude), Is.EqualTo(@"D:\tools\claude.exe"));
        }
    }

    [Test]
    public void Настройки_бэкендов_не_затирают_друг_друга()
    {
        var store = Open();

        var preferences = new AgentPreferences(store)
        {
            Model = "sonnet",
            CliPath = @"D:\tools\claude.exe",
            Backend = AgentBackendKind.Codex,
        };

        preferences.Model = "gpt-5.6";
        preferences.CliPath = @"D:\tools\codex.exe";
        store.Flush();

        var reopened = new AgentPreferences(Open());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reopened.Backend, Is.EqualTo(AgentBackendKind.Codex));
            Assert.That(reopened.ModelFor(AgentBackendKind.Claude), Is.EqualTo("sonnet"));
            Assert.That(reopened.ModelFor(AgentBackendKind.Codex), Is.EqualTo("gpt-5.6"));
            Assert.That(reopened.CliPathFor(AgentBackendKind.Claude), Is.EqualTo(@"D:\tools\claude.exe"));
            Assert.That(reopened.CliPathFor(AgentBackendKind.Codex), Is.EqualTo(@"D:\tools\codex.exe"));
        }
    }

    [Test]
    public void Смена_бэкенда_подставляет_его_собственные_значения()
    {
        var store = Open();

        var preferences = new AgentPreferences(store) { Model = "sonnet" };
        preferences.Backend = AgentBackendKind.Codex;

        Assert.That(preferences.Model, Is.Empty);

        preferences.Model = "gpt-5.6";
        preferences.Backend = AgentBackendKind.Claude;

        Assert.That(preferences.Model, Is.EqualTo("sonnet"));
    }

    [Test]
    public void Очищенная_модель_не_воскресает_из_прежнего_общего_ключа()
    {
        Seed(store => store.SetValue(SettingsKeys.AgentModelShared, "sonnet"));

        var first = Open();
        var preferences = new AgentPreferences(first) { Model = string.Empty };
        first.Flush();

        Assert.That(new AgentPreferences(Open()).ModelFor(AgentBackendKind.Claude), Is.Empty);
    }

    private void Seed(Action<ISettingsStore> configure)
    {
        var store = Open();
        configure(store);
        store.Flush();
    }

    private ISettingsStore Open()
    {
        return new SettingsStore(Path.Combine(_directory, TomlSettingsFile.PrimaryFileName));
    }
}
