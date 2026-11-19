using KeepShell.Bootstrap;
using SpaceSnoop.Wpf.Agent;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Settings;

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
            Directory.Delete(_directory, true);
        }
        catch (IOException exception)
        {
            TestContext.Out.WriteLine($"Не удалось удалить каталог настроек {_directory}: {exception.Message}");
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
        _ = new AgentPreferences(first)
        {
            Model = string.Empty,
        };

        first.Flush();

        Assert.That(new AgentPreferences(Open()).ModelFor(AgentBackendKind.Claude), Is.Empty);
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
    public void Смена_бэкенда_подставляет_его_собственное_согласие()
    {
        var store = Open();

        var preferences = new AgentPreferences(store)
        {
            Consent = true,
        };

        preferences.Backend = AgentBackendKind.Codex;

        Assert.That(preferences.Consent, Is.False);

        preferences.Consent = true;
        preferences.Backend = AgentBackendKind.OpenCode;

        Assert.That(preferences.Consent, Is.False);

        preferences.Backend = AgentBackendKind.Claude;

        Assert.That(preferences.Consent, Is.True);
    }

    [Test]
    public void Смена_бэкенда_подставляет_его_собственные_значения()
    {
        var store = Open();

        var preferences = new AgentPreferences(store)
        {
            Model = "sonnet",
        };

        preferences.Backend = AgentBackendKind.Codex;

        Assert.That(preferences.Model, Is.Empty);

        preferences.Model = "gpt-5.6";
        preferences.Backend = AgentBackendKind.Claude;

        Assert.That(preferences.Model, Is.EqualTo("sonnet"));
    }

    [Test]
    public void Согласие_известно_уже_к_объявлению_смены_бэкенда()
    {
        var store = Open();

        var preferences = new AgentPreferences(store)
        {
            Consent = true,
        };

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
