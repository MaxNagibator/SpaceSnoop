using SpaceSnoop.Wpf.ViewModels.Settings;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SettingsSectionTests
{
    private static SettingsSection Collapsed()
    {
        return new("MCP-сервер", false, "порт токен подключение");
    }

    [TestCase("mcp")]
    [TestCase("сервер")]
    [TestCase("ТОКЕН")]
    [TestCase("порт токен")]
    public void Раздел_находится_по_заголовку_и_ключевым_словам_без_учёта_регистра(string query)
    {
        var section = Collapsed();

        section.Filter(SettingsSection.ParseQuery(query));

        Assert.That(section.IsVisible, Is.True);
    }

    [TestCase("тема оформления")]
    [TestCase("токен тема")]
    public void Несовпавший_раздел_прячется_а_слова_запроса_складываются_по_И(string query)
    {
        var section = Collapsed();

        section.Filter(SettingsSection.ParseQuery(query));

        Assert.That(section.IsVisible, Is.False);
    }

    [Test]
    public void Найденный_раздел_разворачивается_даже_если_свёрнут_по_умолчанию()
    {
        var section = Collapsed();

        section.Filter(SettingsSection.ParseQuery("токен"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(section.IsVisible, Is.True);
            Assert.That(section.IsExpanded, Is.True);
        }
    }

    [Test]
    public void Пустой_запрос_возвращает_раздел_к_состоянию_по_умолчанию()
    {
        var section = Collapsed();
        section.Filter(SettingsSection.ParseQuery("токен"));

        section.Filter(SettingsSection.ParseQuery(string.Empty));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(section.IsVisible, Is.True);
            Assert.That(section.IsExpanded, Is.False);
        }
    }
}
