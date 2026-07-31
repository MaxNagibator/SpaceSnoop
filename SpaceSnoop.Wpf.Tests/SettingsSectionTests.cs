using MahApps.Metro.IconPacks;
using SpaceSnoop.Wpf.ViewModels.Settings;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SettingsSectionTests
{
    private static SettingsSectionList List()
    {
        return new(
            new SettingsSection("appearance", "Внешний вид", PackIconLucideKind.Palette, "тема оформления масштаб шрифта"),
            new SettingsSection("mcp", "MCP-сервер", PackIconLucideKind.Plug, "порт токен подключение"),
            new SettingsSection("agent", "Агент-чат", PackIconLucideKind.MessageCircle, "claude codex модель"));
    }

    [TestCase("mcp")]
    [TestCase("сервер")]
    [TestCase("ТОКЕН")]
    [TestCase("порт токен")]
    public void Раздел_находится_по_заголовку_и_ключевым_словам_без_учёта_регистра(string query)
    {
        var sections = List();

        sections.Filter(query);

        Assert.That(sections["mcp"].IsVisible, Is.True);
    }

    [TestCase("тема оформления")]
    [TestCase("токен тема")]
    public void Несовпавший_раздел_прячется_а_слова_запроса_складываются_по_И(string query)
    {
        var sections = List();

        sections.Filter(query);

        Assert.That(sections["mcp"].IsVisible, Is.False);
    }

    [Test]
    public void Первый_раздел_выбран_сразу_после_создания()
    {
        var sections = List();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sections.Selected, Is.SameAs(sections["appearance"]));
            Assert.That(sections["appearance"].IsSelected, Is.True);
        }
    }

    [Test]
    public void Выбор_другого_раздела_снимает_отметку_с_прежнего()
    {
        var sections = List();

        sections.Selected = sections["mcp"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sections["appearance"].IsSelected, Is.False);
            Assert.That(sections["mcp"].IsSelected, Is.True);
        }
    }

    [Test]
    public void Поиск_переводит_выбор_на_первый_найденный_раздел_если_прежний_скрыт()
    {
        var sections = List();

        sections.Filter("токен");

        Assert.That(sections.Selected, Is.SameAs(sections["mcp"]));
    }

    [Test]
    public void Поиск_оставляет_выбор_если_раздел_нашёлся()
    {
        var sections = List();
        sections.Selected = sections["agent"];

        sections.Filter("модель");

        Assert.That(sections.Selected, Is.SameAs(sections["agent"]));
    }

    [Test]
    public void Ничего_не_найдено_снимает_выбор_и_поднимает_флаг()
    {
        var sections = List();

        sections.Filter("гидропоника");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sections.NoMatches, Is.True);
            Assert.That(sections.Selected, Is.Null);
        }
    }

    [Test]
    public void Пустой_запрос_возвращает_видимость_всем_разделам()
    {
        var sections = List();
        sections.Filter("токен");

        sections.Filter(string.Empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sections.Items.All(section => section.IsVisible), Is.True);
            Assert.That(sections.Selected, Is.SameAs(sections["mcp"]));
        }
    }
}
