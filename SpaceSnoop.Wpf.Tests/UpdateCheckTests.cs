using KeepShell.Services;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Schedule;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.About;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.ViewModels.Settings;
using System.Net;
using System.Text.Json;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class UpdateCheckTests
{
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

        var items = ReleaseChangelog.ExtractChangeGroups(body).Single().Items;

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
    public void История_изменений_не_берёт_технические_изменения_релиза()
    {
        var body = """
            ## Скачать

            EXE ZIP Portable

            ## Изменения

            - Скан стал вдвое быстрее

            ## Технические изменения

            <details>
            <summary>Коммиты (2)</summary>

            - Каркас обновлён до 0.1.141
            - Привязанные коллекции заведены на поток диспетчера

            </details>

            **Полный список:** https://github.com/x/y/compare/v2.8.70...v2.8.71
            """;

        var items = ReleaseChangelog.ExtractChangeGroups(body).Single().Items;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].Summary, Is.EqualTo("Скан стал вдвое быстрее"));
            Assert.That(ReleaseChangelog.ExtractCompareUrl(body), Is.EqualTo("https://github.com/x/y/compare/v2.8.70...v2.8.71"));
        }
    }

    [TestCase(HttpStatusCode.Forbidden, true)]
    [TestCase(HttpStatusCode.TooManyRequests, true)]
    [TestCase(HttpStatusCode.NotFound, false)]
    public void Ограничение_GitHub_узнаётся_по_коду_ответа(HttpStatusCode status, bool expected)
    {
        var exception = new HttpRequestException("ответ ленты", null, status);

        Assert.That(AppUpdateViewModel.IsRateLimited(exception), Is.EqualTo(expected));
    }

    [Test]
    public void Подбор_ассета_для_framework_dependent_берёт_exe_своего_рантайма_без_portable_и_classic()
    {
        string[] assets =
        [
            "SpaceSnoop-v2.9.0.exe",
            "SpaceSnoop-v2.9.0.zip",
            "SpaceSnoop-v2.9.0-portable.zip",
            "SpaceSnoop-Net11-v2.9.0.exe",
            "SpaceSnoop-Net11-v2.9.0.zip",
            "SpaceSnoop-Net11-v2.9.0-portable.zip",
            "SpaceSnoop-Classic-v2.9.0.exe",
        ];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(UpdateCheck.PickAsset(assets, "SpaceSnoop", false), Is.EqualTo("SpaceSnoop-v2.9.0.exe"));
            Assert.That(UpdateCheck.PickAsset(assets, "SpaceSnoop-Net11", false), Is.EqualTo("SpaceSnoop-Net11-v2.9.0.exe"));
        }
    }

    [Test]
    public void Подбор_ассета_для_self_contained_берёт_portable_zip_своего_рантайма()
    {
        string[] assets =
        [
            "SpaceSnoop-v2.9.0.exe",
            "SpaceSnoop-v2.9.0-portable.zip",
            "SpaceSnoop-Net11-v2.9.0.exe",
            "SpaceSnoop-Net11-v2.9.0-portable.zip",
            "SpaceSnoop-Classic-v2.9.0-portable.zip",
        ];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(UpdateCheck.PickAsset(assets, "SpaceSnoop", true), Is.EqualTo("SpaceSnoop-v2.9.0-portable.zip"));
            Assert.That(UpdateCheck.PickAsset(assets, "SpaceSnoop-Net11", true), Is.EqualTo("SpaceSnoop-Net11-v2.9.0-portable.zip"));
        }
    }

    [Test]
    public void Подбор_ассета_не_путает_соседние_редакции_между_собой()
    {
        string[] classicAndNet11 =
        [
            "SpaceSnoop-Net11-v2.9.0.exe",
            "SpaceSnoop-Net11-v2.9.0-portable.zip",
            "SpaceSnoop-Classic-v2.9.0.exe",
            "SpaceSnoop-Classic-v2.9.0-portable.zip",
        ];
        string[] net10Only =
        [
            "SpaceSnoop-v2.9.0.exe",
            "SpaceSnoop-v2.9.0-portable.zip",
        ];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(UpdateCheck.PickAsset(classicAndNet11, "SpaceSnoop", false), Is.Null);
            Assert.That(UpdateCheck.PickAsset(classicAndNet11, "SpaceSnoop", true), Is.Null);
            Assert.That(UpdateCheck.PickAsset(net10Only, "SpaceSnoop-Net11", false), Is.Null);
            Assert.That(UpdateCheck.PickAsset(net10Only, "SpaceSnoop-Net11", true), Is.Null);
        }
    }

    [Test]
    public void Продукт_ассетов_соответствует_целевому_рантайму_сборки()
    {
#if NET11_0_OR_GREATER
        const string expected = "SpaceSnoop-Net11";
#else
        const string expected = "SpaceSnoop";
#endif
        Assert.That(AppInfo.ReleaseAssetProduct, Is.EqualTo(expected));
    }

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
    public async Task Скачивание_без_известного_размера_молчит()
    {
        var reports = new List<int>();

        await ReleaseFeed.CopyAsync(new MemoryStream(new byte[1024 * 1024]), Stream.Null, -1, new PercentCollector(reports));

        Assert.That(reports, Is.Empty);
    }

    [Test]
    public async Task Скачивание_докладывает_только_смену_целого_процента()
    {
        const int megabytes = 16;
        var payload = new byte[megabytes * 1024 * 1024];
        var reports = new List<int>();

        await ReleaseFeed.CopyAsync(new MemoryStream(payload), Stream.Null, payload.Length, new PercentCollector(reports));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reports, Has.Count.LessThanOrEqualTo(101), $"Докладов {reports.Count} на 101 значение процента – отчёт снова идёт на каждый блок.");
            Assert.That(reports, Is.Ordered.Ascending.And.Unique);
            Assert.That(reports[^1], Is.EqualTo(100), "Последний процент до экрана не доехал.");
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
    public void Строка_полного_списка_пропускается_в_пунктах_изменений()
    {
        var body = """
            ## Изменения

            - Первый пункт
            **Полный список:** https://github.com/x/y/compare/v1...v2
            - Второй пункт
            """;

        var items = ReleaseChangelog.ExtractChangeGroups(body).Single().Items;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Summary, Is.EqualTo("Первый пункт"));
            Assert.That(items[1].Summary, Is.EqualTo("Второй пункт"));
        }
    }

    [Test]
    public void Тело_без_секции_изменений_берётся_целиком_а_первая_строка_становится_пунктом()
    {
        var body = """
            Просто текст без заголовков.
            Вторая строка.
            """;

        var items = ReleaseChangelog.ExtractChangeGroups(body).Single().Items;

        using (Assert.EnterMultipleScope())
        {
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

        Assert.That(ReleaseChangelog.ExtractChangeGroups(body), Is.Empty);
    }

    [Test]
    public void Подзаголовки_разделов_группируют_изменения_и_не_склеиваются_в_подробности_предыдущего_пункта()
    {
        var body = """
            ## Изменения

            ### Сканирование и дерево

            - В дереве можно выделять сразу несколько папок и файлов.
            - Выгрузку результата сканирования в JSON можно остановить по кнопке или клавише Escape.

            ### Очистка и разработка

            - В «Очистке» появился раздел «Разработка».
            """;

        var groups = ReleaseChangelog.ExtractChangeGroups(body);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(groups, Has.Count.EqualTo(2));
            Assert.That(groups[0].Title, Is.EqualTo("Сканирование и дерево"));
            Assert.That(groups[0].Items, Has.Count.EqualTo(2));
            Assert.That(groups[0].Items[0].Details, Is.Empty);
            Assert.That(groups[0].Items[1].Details, Is.Empty);

            Assert.That(groups[1].Title, Is.EqualTo("Очистка и разработка"));
            Assert.That(groups[1].Items, Has.Count.EqualTo(1));
            Assert.That(groups[1].Items[0].Summary, Is.EqualTo("В «Очистке» появился раздел «Разработка»."));
        }
    }

    [Test]
    public void Модальное_окно_обновления_строит_группы_и_пункты_без_обрезки_по_длине_и_с_глагольными_кнопками()
    {
        var longParagraph = new string('А', 900);
        const string tailItem = "Выгрузку результата сканирования в JSON теперь можно остановить по кнопке или клавише Escape.";

        var body = $"""
            ## Изменения

            ### Сканирование и дерево

            - {longParagraph}
            - {longParagraph}

            ### Интерфейс и настройки

            - {tailItem}
            """;

        var groups = ReleaseChangelog.ExtractChangeGroups(body);
        IReadOnlyList<ReleaseNoteViewModel> entries =
        [
            new("v99.9.0", "2026-11-09", groups.SelectMany(static g => g.Items).ToList(), "https://github.com/x/y/compare/v99.8.0...v99.9.0", groups),
        ];

        var withAsset = ReleaseChangelog.BuildUpdateDialog(
            "v99.9.0",
            "SpaceSnoop-v99.9.0.exe",
            "https://github.com/x/y/releases/download/v99.9.0/SpaceSnoop-v99.9.0.exe",
            entries,
            string.Empty);
        var withoutAsset = ReleaseChangelog.BuildUpdateDialog("v99.9.0", null, null, entries, string.Empty);

        var plainText = ConfirmDialogViewModel.AsText(withAsset.Lines);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(withAsset.Title, Is.EqualTo("Доступно обновление"));
            Assert.That(withAsset.Choices.Select(static choice => choice.Caption), Is.EqualTo(["Не сейчас", "Открыть на GitHub", "Скачать"]));
            Assert.That(withoutAsset.Choices.Select(static choice => choice.Caption), Is.EqualTo(["Не сейчас", "Открыть на GitHub"]));
            Assert.That(plainText, Does.Contain("Сканирование и дерево"));
            Assert.That(plainText, Does.Contain("Интерфейс и настройки"));
            Assert.That(plainText, Does.Contain(tailItem));
            Assert.That(plainText, Does.Not.Contain("JSON те…"));
        }
    }

    [Test]
    public void Копирование_сведений_и_навигация_на_странице_О_программе_уведомляют_и_переходят_в_раздел()
    {
        var settings = new MemorySettings();
        var preferences = new UpdatePreferences(settings)
        {
            Repository = "custom/spacesnoop",
        };
        var shell = new StubShellLauncher();
        var clipboard = new StubClipboardService { Succeeds = true };
        var navigator = new StubAppNavigator();
        var toasts = new ToastHostViewModel();
        var notifier = new ToastNotifier(toasts, new ShellPreferences(settings));
        var updater = new AppUpdateViewModel(preferences, settings, new NoopDialogs(), shell, NullLogger<AppUpdateViewModel>.Instance);
        var errorReports = new ErrorReportService(new() { IssueRepo = () => AppInfo.RepoSlug }, new(), NullLogger<ErrorReportService>.Instance);

        var about = new AboutViewModel(errorReports, updater, navigator, notifier, clipboard, shell, NullLogger<AboutViewModel>.Instance);

        about.CopyDiagnosticsCommand.Execute(null);
        var firstToast = toasts.Toasts.LastOrDefault();

        clipboard.Succeeds = false;
        about.CopyDiagnosticsCommand.Execute(null);
        var secondToast = toasts.Toasts.LastOrDefault();

        about.OpenSectionCommand.Execute(SectionKey.Cleanup);
        var firstSection = navigator.CurrentSectionKey;

        navigator.RefuseChat = true;
        about.OpenSectionCommand.Execute(SectionKey.Chat);
        var fallbackSection = navigator.CurrentSectionKey;

        about.OpenRepositoryCommand.Execute(null);
        about.OpenReleasesCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(clipboard.LastText, Does.Contain(AppInfo.Version));
            Assert.That(firstToast?.Severity, Is.EqualTo(StatusSeverity.Success));
            Assert.That(secondToast?.Severity, Is.EqualTo(StatusSeverity.Warning));
            Assert.That(firstSection, Is.EqualTo(SectionKey.Cleanup));
            Assert.That(fallbackSection, Is.EqualTo(SectionKey.Settings));
            Assert.That(shell.OpenedUrls, Is.EqualTo(["https://github.com/custom/spacesnoop", "https://github.com/custom/spacesnoop/releases"]));
        }
    }

    private sealed class PercentCollector(List<int> sink) : IProgress<int>
    {
        public void Report(int value)
        {
            sink.Add(value);
        }
    }

    private sealed class StubShellLauncher : IShellLauncher
    {
        public List<string> OpenedUrls { get; } = [];

        public bool Open(string pathOrUrl)
        {
            OpenedUrls.Add(pathOrUrl);
            return true;
        }

        public bool Reveal(string path)
        {
            return true;
        }

        public bool Start(string executable, params string[] arguments)
        {
            return true;
        }
    }

    private sealed class StubClipboardService : IClipboardService
    {
        public bool Succeeds { get; set; } = true;

        public string? LastText { get; private set; }

        public bool TrySetText(string? text)
        {
            LastText = text;
            return Succeeds;
        }
    }

    private sealed class StubAppNavigator : IAppNavigator
    {
        public bool RefuseChat { get; set; }

        public string? CurrentSectionKey { get; private set; }

        public bool TryNavigate(string sectionKey)
        {
            if (RefuseChat && string.Equals(sectionKey, SectionKey.Chat, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            CurrentSectionKey = sectionKey;
            return true;
        }

        public void OpenSync(SyncProfile profile, ComparisonResult? comparison)
        {
        }

        public void AskAgent(string question)
        {
        }
    }
}
