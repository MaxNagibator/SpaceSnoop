using SpaceSnoop.Wpf.Agent;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Settings;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class AgentModelSelectorTests
{
    [Test]
    public void Выбор_модели_из_списка_пишется_в_настройки()
    {
        var settings = new MemorySettings();
        var preferences = new AgentPreferences(settings);
        var selector = new AgentModelSelector(preferences);

        selector.SelectedModel = selector.ModelOptions.First(option => option.Id == "sonnet");

        Assert.That(settings.GetStringValue(SettingsKeys.AgentModel(AgentBackendKind.Claude)), Is.EqualTo("sonnet"));
    }

    [Test]
    public void Слаг_вне_каталога_становится_отдельным_пунктом_списка()
    {
        var preferences = new AgentPreferences(new MemorySettings()) { Model = "claude-opus-5-мой" };
        var selector = new AgentModelSelector(preferences);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selector.SelectedModel.Id, Is.EqualTo("claude-opus-5-мой"));
            Assert.That(selector.SelectedModel.Hint, Is.EqualTo("Задана вручную"));
        }
    }

    [Test]
    public void Уровень_недоступный_бэкенду_сбрасывается_в_умолчание()
    {
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.AgentEffort(AgentBackendKind.Claude), "ultra");

        var preferences = new AgentPreferences(settings);
        var selector = new AgentModelSelector(preferences);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selector.SelectedEffort, Is.EqualTo(AgentModels.EffortDefault));
            Assert.That(preferences.Effort, Is.Empty);
            Assert.That(settings.GetStringValue(SettingsKeys.AgentEffort(AgentBackendKind.Claude)), Is.Empty);
        }
    }

    [Test]
    public void Выбор_уровня_пишется_в_ключ_своего_бэкенда()
    {
        var settings = new MemorySettings();
        var preferences = new AgentPreferences(settings) { Backend = AgentBackendKind.Codex };
        var selector = new AgentModelSelector(preferences);

        selector.SelectedEffort = selector.EffortOptions.First(option => option.Id == "ultra");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings.GetStringValue(SettingsKeys.AgentEffort(AgentBackendKind.Codex)), Is.EqualTo("ultra"));
            Assert.That(settings.GetStringValue(SettingsKeys.AgentEffort(AgentBackendKind.Claude)), Is.Null);
        }
    }

    [Test]
    public void Смена_бэкенда_переставляет_список_моделей()
    {
        var preferences = new AgentPreferences(new MemorySettings());
        var selector = new AgentModelSelector(preferences);

        var claude = selector.ModelOptions.Select(option => option.Id).ToArray();
        preferences.Backend = AgentBackendKind.Codex;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(claude, Does.Contain("sonnet"));
            Assert.That(selector.ModelOptions.Select(option => option.Id), Does.Not.Contain("sonnet"));
            Assert.That(selector.SelectedModel, Is.EqualTo(AgentModels.CliDefault));
        }
    }
}
