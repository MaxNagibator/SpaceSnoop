using SpaceSnoop.Wpf.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Text.Json;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class DiagnosticsBundleTests
{
    [TestCase(@"C:\Users\admin\Downloads\отчёт.txt", @"C:\…\отчёт.txt")]
    [TestCase(@"D:\Проекты", @"D:\Проекты")]
    [TestCase(@"D:\Проекты\Сайт", @"D:\…\Сайт")]
    [TestCase(@"\\сервер\общая\папка\файл.log", @"\\сервер\общая\…\файл.log")]
    [TestCase(@"\\?\UNC\finance-nas\private\alice\report.xlsx", @"\\?\UNC\finance-nas\private\…\report.xlsx")]
    [TestCase(@"\\?\C:\Users\admin\отчёт.txt", @"\\?\C:\…\отчёт.txt")]
    public void Из_пути_остаются_корень_и_имя(string path, string expected)
    {
        Assert.That(new DiagnosticsRedactor().Apply(path), Is.EqualTo(expected));
    }

    [Test]
    public void Путь_внутри_JSON_обезличивается_вместе_с_экранированием()
    {
        var json = JsonSerializer.Serialize(new { path = @"C:\Users\admin\AppData\Local\SpaceSnoop\logs\wpf.log" });

        var redacted = new DiagnosticsRedactor().Apply(json);

        Assert.That(redacted, Does.Not.Contain("admin"));
        Assert.That(redacted, Does.Contain(@"C:\\…\\wpf.log"));
    }

    [Test]
    public void Имя_пользователя_и_имя_машины_заменяются_масками()
    {
        var redacted = new DiagnosticsRedactor("admin", "HOMEPC").Apply("Пользователь admin на машине homepc");

        Assert.That(redacted, Is.EqualTo($"Пользователь {DiagnosticsRedactor.UserMask} на машине {DiagnosticsRedactor.MachineMask}"));
    }

    [Test]
    public void Без_обезличивания_текст_едет_как_есть()
    {
        var entries = DiagnosticsBundle.Build(Payload(), null);

        Assert.That(Text(entries, "logs/wpf-20260831.log"), Is.EqualTo(@"скан C:\Users\admin\Музыка"));
    }

    [Test]
    public void Пакет_несёт_профиль_замеры_сводку_настройки_и_журналы()
    {
        var entries = DiagnosticsBundle.Build(Payload(), DiagnosticsRedactor.ForCurrentUser());

        Assert.That(entries.Select(entry => entry.Name), Is.EqualTo(new[]
        {
            DiagnosticsBundle.MachineEntry,
            DiagnosticsBundle.PerformanceEntry,
            DiagnosticsBundle.SummaryEntry,
            DiagnosticsBundle.SettingsEntry,
            "logs/wpf-20260831.log",
        }));
    }

    [Test]
    public void Обезличивание_доходит_до_каждой_записи_пакета()
    {
        var entries = DiagnosticsBundle.Build(Payload(), new DiagnosticsRedactor("admin"));

        Assert.That(entries.All(entry => !entry.Text.Contains("admin", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(Text(entries, DiagnosticsBundle.SettingsEntry), Does.Not.Contain("Users"));
    }

    [Test]
    public void Сохранённый_архив_читается_записями_пакета()
    {
        var directory = Path.Combine(Path.GetTempPath(), "spacesnoop-diagnostics-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var entries = DiagnosticsBundle.Build(Payload(), null);
            var path = DiagnosticsBundle.Save(directory, entries, new DateTime(2026, 8, 31, 19, 5, 0, DateTimeKind.Local));

            Assert.That(Path.GetFileName(path), Is.EqualTo("spacesnoop-diagnostics-20260831-190500.zip"));

            using var archive = ZipFile.OpenRead(path);

            Assert.That(archive.Entries.Select(entry => entry.FullName), Is.EqualTo(entries.Select(entry => entry.Name)));

            using var reader = new StreamReader(archive.GetEntry(DiagnosticsBundle.SummaryEntry)!.Open());

            Assert.That(reader.ReadToEnd(), Does.Contain("Процессор:"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Test]
    public void Расширенный_UNC_внутри_JSON_обезличивается()
    {
        var json = JsonSerializer.Serialize(new { path = @"\\?\UNC\finance-nas\private\alice\report.xlsx" });

        var redacted = new DiagnosticsRedactor().Apply(json);

        Assert.That(redacted, Does.Not.Contain("alice"));
        Assert.That(redacted, Does.Contain(DiagnosticsRedactor.Ellipsis));
    }

    [Test]
    public void Имя_маскируется_и_под_турецкой_культурой()
    {
        var culture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new("tr-TR");

            var redacted = new DiagnosticsRedactor("FILE", "HOMEPC").Apply("пользователь file");

            Assert.That(redacted, Is.EqualTo($"пользователь {DiagnosticsRedactor.UserMask}"));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Test]
    public void Токен_вырезается_в_обоих_режимах()
    {
        var payload = Payload() with { Settings = "[wpf.mcp]" + Environment.NewLine + "token = " + '"' + "s3cr3t-live-token" + '"' };

        foreach (var redactor in new DiagnosticsRedactor?[] { null, DiagnosticsRedactor.ForCurrentUser() })
        {
            var entries = DiagnosticsBundle.Build(payload, redactor);

            Assert.That(Text(entries, DiagnosticsBundle.SettingsEntry), Does.Not.Contain("s3cr3t-live-token"));
            Assert.That(Text(entries, DiagnosticsBundle.SettingsEntry), Does.Contain(DiagnosticsSecrets.Mask));
        }
    }

    [Test]
    public void Токен_вырезается_и_из_журнала()
    {
        var payload = Payload() with { Logs = [new("wpf-20260831.log", "api_key: abcdef123456")] };

        var entries = DiagnosticsBundle.Build(payload, null);

        Assert.That(Text(entries, "logs/wpf-20260831.log"), Does.Not.Contain("abcdef123456"));
    }

    [Test]
    public void Пропавшие_настройки_названы_в_составе_пакета()
    {
        var entries = DiagnosticsBundle.Build(Payload() with { Settings = null }, null);

        Assert.That(DiagnosticsBundle.Describe(entries, true), Does.Contain("Настройки в пакет не попали"));
    }

    [Test]
    public void Второй_пакет_в_ту_же_секунду_не_затирает_первый()
    {
        var directory = Path.Combine(Path.GetTempPath(), "spacesnoop-diagnostics-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var entries = DiagnosticsBundle.Build(Payload(), null);
            var now = new DateTime(2026, 8, 31, 19, 5, 0, DateTimeKind.Local);

            var first = DiagnosticsBundle.Save(directory, entries, now);
            var second = DiagnosticsBundle.Save(directory, entries, now);

            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(File.Exists(first), Is.True);
            Assert.That(File.Exists(second), Is.True);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static DiagnosticsPayload Payload()
    {
        return new(
            MachineProfile.Capture(),
            PerformanceSnapshot.Empty,
            PerformanceHistory.Empty,
            PerformanceHitches.Empty,
            null,
            @"exclusions = ""C:\Users\admin\Музыка""",
            [new("wpf-20260831.log", @"скан C:\Users\admin\Музыка")]);
    }

    private static string Text(IReadOnlyList<DiagnosticsEntry> entries, string name)
    {
        return entries.Single(entry => entry.Name == name).Text;
    }
}
