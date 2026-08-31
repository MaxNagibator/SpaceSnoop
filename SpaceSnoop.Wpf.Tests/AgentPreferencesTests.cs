using KeepShell.Bootstrap;
using SpaceSnoop.Wpf.Agent;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
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
        catch (IOException exception)
        {
            TestContext.Out.WriteLine($"Не удалось удалить каталог настроек {_directory}: {exception.Message}");
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
        preferences.Backend = AgentBackendKind.OpenCode;
        preferences.Model = "opencode/big-pickle";
        preferences.CliPath = @"D:\tools\opencode.exe";
        store.Flush();

        var reopened = new AgentPreferences(Open());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reopened.Backend, Is.EqualTo(AgentBackendKind.OpenCode));
            Assert.That(reopened.ModelFor(AgentBackendKind.Claude), Is.EqualTo("sonnet"));
            Assert.That(reopened.ModelFor(AgentBackendKind.Codex), Is.EqualTo("gpt-5.6"));
            Assert.That(reopened.ModelFor(AgentBackendKind.OpenCode), Is.EqualTo("opencode/big-pickle"));
            Assert.That(reopened.CliPathFor(AgentBackendKind.Claude), Is.EqualTo(@"D:\tools\claude.exe"));
            Assert.That(reopened.CliPathFor(AgentBackendKind.Codex), Is.EqualTo(@"D:\tools\codex.exe"));
            Assert.That(reopened.CliPathFor(AgentBackendKind.OpenCode), Is.EqualTo(@"D:\tools\opencode.exe"));
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
    public void Глубина_рассуждений_живёт_в_ключе_своего_бэкенда()
    {
        var store = Open();

        var preferences = new AgentPreferences(store) { Effort = "xhigh", Backend = AgentBackendKind.Codex };

        preferences.Effort = "ultra";
        store.Flush();

        var reopened = new AgentPreferences(Open());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reopened.EffortFor(AgentBackendKind.Claude), Is.EqualTo("xhigh"));
            Assert.That(reopened.EffortFor(AgentBackendKind.Codex), Is.EqualTo("ultra"));
            Assert.That(reopened.Effort, Is.EqualTo("ultra"));
        }
    }

    [Test]
    public void Согласие_одного_бэкенда_не_распространяется_на_другие()
    {
        var store = Open();

        _ = new AgentPreferences(store) { Consent = true };

        store.Flush();

        var reopened = new AgentPreferences(Open());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reopened.ConsentFor(AgentBackendKind.Claude), Is.True);
            Assert.That(reopened.ConsentFor(AgentBackendKind.Codex), Is.False);
            Assert.That(reopened.ConsentFor(AgentBackendKind.OpenCode), Is.False);
        }
    }

    [Test]
    public void Смена_бэкенда_подставляет_его_собственное_согласие()
    {
        var store = Open();

        var preferences = new AgentPreferences(store) { Consent = true };

        preferences.Backend = AgentBackendKind.Codex;

        Assert.That(preferences.Consent, Is.False);

        preferences.Consent = true;
        preferences.Backend = AgentBackendKind.OpenCode;

        Assert.That(preferences.Consent, Is.False);

        preferences.Backend = AgentBackendKind.Claude;

        Assert.That(preferences.Consent, Is.True);
    }

    [Test]
    public void Согласие_известно_уже_к_объявлению_смены_бэкенда()
    {
        var store = Open();

        var preferences = new AgentPreferences(store) { Consent = true };

        bool? consentAtBackendNotice = null;

        preferences.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AgentPreferences.Backend))
            {
                consentAtBackendNotice = preferences.Consent;
            }
        };

        preferences.Backend = AgentBackendKind.Codex;

        Assert.That(consentAtBackendNotice, Is.False);
    }

    [Test]
    public void Прежнее_общее_согласие_достаётся_только_Claude()
    {
        Seed(store => store.SetValue(SettingsKeys.AgentConsentShared, "true"));

        var first = Open();
        var preferences = new AgentPreferences(first);

        first.Flush();

        var second = Open();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preferences.ConsentFor(AgentBackendKind.Claude), Is.True);
            Assert.That(preferences.ConsentFor(AgentBackendKind.Codex), Is.False);
            Assert.That(preferences.ConsentFor(AgentBackendKind.OpenCode), Is.False);
            Assert.That(second.GetStringValue(SettingsKeys.AgentConsentShared), Is.Empty);
            Assert.That(new AgentPreferences(second).ConsentFor(AgentBackendKind.Claude), Is.True);
        }
    }

    [TestCase("true", "true", ExpectedResult = true)]
    [TestCase("true", "false", ExpectedResult = false)]
    [TestCase("false", "true", ExpectedResult = false)]
    [TestCase("false", "false", ExpectedResult = false)]
    public bool Отзыв_согласия_переживает_встречу_общего_ключа_с_ключом_бэкенда(string shared, string backend)
    {
        Seed(store =>
        {
            store.SetValue(SettingsKeys.AgentConsentShared, shared);
            store.SetValue(SettingsKeys.AgentConsent(AgentBackendKind.Claude), backend);
        });

        return new AgentPreferences(Open()).ConsentFor(AgentBackendKind.Claude);
    }

    [Test]
    public void Очищенная_модель_не_воскресает_из_прежнего_общего_ключа()
    {
        Seed(store => store.SetValue(SettingsKeys.AgentModelShared, "sonnet"));

        var first = Open();
        _ = new AgentPreferences(first) { Model = string.Empty };
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
