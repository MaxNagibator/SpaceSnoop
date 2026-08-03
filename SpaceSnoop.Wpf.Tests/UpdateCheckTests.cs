using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Settings;
using System.Text.Json;

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

        Assert.That(ReleaseChangelog.ExtractChanges(body), Is.EqualTo("- Добавлена карта\r\n  Карта теперь рисуется пиксельным буфером.\r\n- Исправлена синхронизация"));
        var items = ReleaseChangelog.ExtractChangeItems(body);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Summary, Is.EqualTo("Добавлена карта"));
            Assert.That(items[0].Details, Is.EqualTo(["Карта теперь рисуется пиксельным буфером."]));
            Assert.That(items[1].Summary, Is.EqualTo("Исправлена синхронизация"));
            Assert.That(items[1].Details, Is.Empty);
        }
    }

    [Test]
    public void Тело_без_секции_изменений_берётся_целиком_а_первая_строка_становится_пунктом()
    {
        var body = """
                   Просто текст без заголовков.
                   Вторая строка.
                   """;

        var items = ReleaseChangelog.ExtractChangeItems(body);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReleaseChangelog.ExtractChanges(body), Is.EqualTo("Просто текст без заголовков.\r\nВторая строка."));
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].Summary, Is.EqualTo("Просто текст без заголовков."));
            Assert.That(items[0].Details, Is.EqualTo(["Вторая строка."]));
        }
    }

    [Test]
    public void Тело_без_секции_изменений_не_тащит_таблицу_скачивания_и_статистику_в_пункты()
    {
        var body = """
                   ## Скачать

                   | Редакция | EXE |
                   |----------|-----|
                   | WPF | [![EXE](badge)](url) |

                   ## Статистика

                   Скачиваний: [![всего](badge)](url)
                   """;

        Assert.That(ReleaseChangelog.ExtractChanges(body), Is.Empty);
        Assert.That(ReleaseChangelog.ExtractChangeItems(body), Is.Empty);
    }

    [Test]
    public void Строка_полного_списка_пропускается_в_пунктах_изменений()
    {
        var body = """
                   ## Изменения

                   - Первый пункт
                   **Полный список:** https://github.com/x/y/compare/v1...v2
                   - Второй пункт
                   """;

        var items = ReleaseChangelog.ExtractChangeItems(body);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Summary, Is.EqualTo("Первый пункт"));
            Assert.That(items[1].Summary, Is.EqualTo("Второй пункт"));
        }
    }

    [TestCase("До: https://github.com/x/y/compare/v1...v2.", "https://github.com/x/y/compare/v1...v2")]
    [TestCase("Без ссылки сравнения", null)]
    [TestCase(null, null)]
    public void Ссылка_сравнения_извлекается_из_тела_релиза(string? body, string? expected)
    {
        Assert.That(ReleaseChangelog.ExtractCompareUrl(body), Is.EqualTo(expected));
    }

    [Test]
    public void Записи_истории_заполняют_дату_заголовок_и_ссылку_с_запасными_значениями()
    {
        const string json = """
            [
              {
                "name": "Версия 2.9.0",
                "tag_name": "v2.9.0",
                "published_at": "2026-06-25T00:57:33Z",
                "html_url": "https://github.com/x/y/releases/tag/v2.9.0",
                "body": "## Изменения\n\n- Новое\n\nhttps://github.com/x/y/compare/v2.8.0...v2.9.0"
              },
              {
                "tag_name": "v2.8.0",
                "published_at": "",
                "html_url": "https://github.com/x/y/releases/tag/v2.8.0",
                "body": "Без секции изменений и без сравнения"
              },
              {
                "name": "Пустой",
                "tag_name": "v2.7.0",
                "html_url": "https://github.com/x/y/releases/tag/v2.7.0",
                "body": ""
              }
            ]
            """;

        using var document = JsonDocument.Parse(json);
        var entries = ReleaseChangelog.BuildChangelogEntries(document.RootElement);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries, Has.Count.EqualTo(3));

            Assert.That(entries[0].Title, Is.EqualTo("Версия 2.9.0"));
            Assert.That(entries[0].PublishedDate, Is.EqualTo("2026-06-25"));
            Assert.That(entries[0].CompareUrl, Is.EqualTo("https://github.com/x/y/compare/v2.8.0...v2.9.0"));

            Assert.That(entries[1].Title, Is.EqualTo("v2.8.0"));
            Assert.That(entries[1].PublishedDate, Is.EqualTo("без даты"));
            Assert.That(entries[1].CompareUrl, Is.EqualTo("https://github.com/x/y/releases/tag/v2.8.0"));

            Assert.That(entries[2].Changes, Has.Count.EqualTo(1));
            Assert.That(entries[2].Changes[0].Summary, Is.EqualTo("Изменения не описаны."));
        }
    }

    [Test]
    public void Заметки_одного_релиза_без_дубля_версии_и_без_markdown()
    {
        const string json = """
            [
              {
                "tag_name": "v99.9.0",
                "body": "## Изменения\n\n- Первый пункт\n\n**Полный список:** https://github.com/x/y/compare/v99.8.0...v99.9.0"
              }
            ]
            """;

        using var document = JsonDocument.Parse(json);
        var notes = ReleaseChangelog.BuildReleaseNotes(document.RootElement);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notes, Does.Not.Contain("**"));
            Assert.That(notes, Does.StartWith("- Первый пункт"));
            Assert.That(notes, Does.Contain("Полный список:"));
        }
    }

    [Test]
    public void Заметки_нескольких_релизов_префиксятся_тегом()
    {
        const string json = """
            [
              { "tag_name": "v99.9.0", "body": "## Изменения\n\n- Новее" },
              { "tag_name": "v99.8.0", "body": "## Изменения\n\n- Старее" }
            ]
            """;

        using var document = JsonDocument.Parse(json);
        var notes = ReleaseChangelog.BuildReleaseNotes(document.RootElement);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notes, Does.Contain("v99.9.0"));
            Assert.That(notes, Does.Contain("v99.8.0"));
        }
    }
}
