using KeepShell.Testing;
using SpaceSnoop.Wpf.ViewModels.Chat;
using SpaceSnoop.Wpf.ViewModels.Settings;

namespace SpaceSnoop.Wpf.Tests;

/// <summary>
/// Понижение доступа посреди хода чата: какие правки настроек обязаны остановить идущий ход, а какие
/// его не касаются.
/// </summary>
[TestFixture]
public class ChatGatesTests
{
    [Test]
    public void Отзыв_согласия_останавливает_идущий_ход()
    {
        var preferences = Preferences(enabled: true, consent: false);

        Assert.That(
            ChatGatesViewModel.CancelsActiveTurn(nameof(AgentPreferences.Consent), preferences),
            Is.True);
    }

    [Test]
    public void Выключение_чата_останавливает_идущий_ход()
    {
        var preferences = Preferences(enabled: false, consent: true);

        Assert.That(
            ChatGatesViewModel.CancelsActiveTurn(nameof(AgentPreferences.Enabled), preferences),
            Is.True);
    }

    [TestCase(nameof(AgentPreferences.Consent))]
    [TestCase(nameof(AgentPreferences.Enabled))]
    [TestCase(nameof(AgentPreferences.Backend))]
    [TestCase(null)]
    public void Ход_переживает_правку_которая_доступа_не_понижает(string? propertyName)
    {
        var preferences = Preferences(enabled: true, consent: true);

        Assert.That(ChatGatesViewModel.CancelsActiveTurn(propertyName, preferences), Is.False);
    }

    private static AgentPreferences Preferences(bool enabled, bool consent)
    {
        var preferences = new AgentPreferences(new MemorySettings())
        {
            Enabled = enabled,
            Consent = consent,
        };

        return preferences;
    }
}
