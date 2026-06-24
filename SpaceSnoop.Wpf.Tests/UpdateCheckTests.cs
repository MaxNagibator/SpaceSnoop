using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Settings;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class UpdateCheckTests
{
    [TestCase("v2.9.0", "2.8.16", true)]
    [TestCase("2.9.0", "2.8.16", true)]
    [TestCase("v2.8.17", "2.8.16", true)]
    [TestCase("v2.8.16", "2.8.16", false)]
    [TestCase("v2.8.2", "2.8.16", false)]
    [TestCase("v2.7.99", "2.8.0", false)]
    [TestCase("v2.9.0-beta", "2.8.16", true)]
    [TestCase(null, "2.8.16", false)]
    [TestCase("", "2.8.16", false)]
    [TestCase("мусор", "2.8.16", false)]
    [TestCase("v2.9.0", "хлам", false)]
    public void Релиз_новее_текущей_версии_только_при_большем_номере(string? latestTag, string current, bool expected)
    {
        Assert.That(UpdateCheck.IsNewer(latestTag, current), Is.EqualTo(expected));
    }

    [Test]
    public void Подбор_ассета_для_framework_dependent_берёт_exe_без_portable_и_classic()
    {
        string[] assets =
        [
            "SpaceSnoop-v2.9.0.exe",
            "SpaceSnoop-v2.9.0.zip",
            "SpaceSnoop-v2.9.0-portable.zip",
            "SpaceSnoop-Classic-v2.9.0.exe",
        ];

        Assert.That(UpdateCheck.PickAsset(assets, "SpaceSnoop", false), Is.EqualTo("SpaceSnoop-v2.9.0.exe"));
    }

    [Test]
    public void Подбор_ассета_для_self_contained_берёт_portable_zip()
    {
        string[] assets =
        [
            "SpaceSnoop-v2.9.0.exe",
            "SpaceSnoop-v2.9.0-portable.zip",
            "SpaceSnoop-Classic-v2.9.0-portable.zip",
        ];

        Assert.That(UpdateCheck.PickAsset(assets, "SpaceSnoop", true), Is.EqualTo("SpaceSnoop-v2.9.0-portable.zip"));
    }

    [Test]
    public void Подбор_ассета_не_путает_classic_с_основной_редакцией()
    {
        string[] assets = ["SpaceSnoop-Classic-v2.9.0.exe", "SpaceSnoop-Classic-v2.9.0-portable.zip"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(UpdateCheck.PickAsset(assets, "SpaceSnoop", false), Is.Null);
            Assert.That(UpdateCheck.PickAsset(assets, "SpaceSnoop", true), Is.Null);
        }
    }

    [Test]
    public void История_изменений_берёт_только_секцию_изменений_из_тела_релиза()
    {
        var body = """
                   ## Скачать

                   EXE ZIP Portable

                   ## Статистика

                   12 скачиваний

                   ## Изменения

                   - Добавлена карта
                     Карта теперь рисуется пиксельным буфером.
                   - Исправлена синхронизация
                   """;

        Assert.That(AppUpdateViewModel.ExtractChanges(body), Is.EqualTo("- Добавлена карта\r\n  Карта теперь рисуется пиксельным буфером.\r\n- Исправлена синхронизация"));
        var items = AppUpdateViewModel.ExtractChangeItems(body);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Summary, Is.EqualTo("Добавлена карта"));
            Assert.That(items[0].Details, Is.EqualTo(["Карта теперь рисуется пиксельным буфером."]));
            Assert.That(items[1].Summary, Is.EqualTo("Исправлена синхронизация"));
            Assert.That(items[1].Details, Is.Empty);
        }
    }
}
