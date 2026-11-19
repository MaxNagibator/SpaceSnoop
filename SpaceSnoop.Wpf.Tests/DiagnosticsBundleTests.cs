using KeepShell.Bootstrap;
using KeepShell.Diagnostics;
using KeepShell.Testing;
using SpaceSnoop.Core;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.ViewModels.Scan;
using System.Text.Json;
using Tomlyn.Model;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class DiagnosticsBundleTests
{
    private const string Token = "s3cr3t-live-token";

    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-diagnostics-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Test]
    public void Живой_токен_вымарывается_из_строки_журнала_Serilog()
    {
        var line = $"2026-09-20 18:00:11.234 +03:00 [INF] Агент подключился по Bearer {Token} за 12 мс";

        var entries = Build(Secrets(Token), [new("wpf-20260920.log", line)]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Text(entries, "logs/wpf-20260920.log"), Does.Not.Contain(Token));
            Assert.That(Text(entries, "logs/wpf-20260920.log"), Does.Contain(DiagnosticsSecrets.Mask));
            Assert.That(Text(entries, "logs/wpf-20260920.log"), Does.Contain("за 12 мс"));
        }
    }

    [Test]
    public void Занятый_файл_настроек_доезжает_до_человека_строкой_об_отказе_чтения()
    {
        var path = Path.Combine(_root, TomlSettingsFile.PrimaryFileName);
        File.WriteAllText(path, "[wpf]");

        var operations = new PerformanceOperations(TestDiagnostics.Monitor());
        var source = new SpaceSnoopBundleSource(new MemorySettings(), operations, path);

        using var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var bundle = Bundle(Secrets(null), source.Collect().ToList());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bundle.Entries.Select(static entry => entry.Name), Does.Not.Contain(SpaceSnoopBundleSource.SettingsEntry));
            Assert.That(
                Text(bundle.Entries, SpaceSnoopBundleSource.SettingsFailureEntry),
                Does.Contain("занят другой программой"));

            Assert.That(bundle.Describe(), Does.Contain(SpaceSnoopBundleSource.SettingsFailureEntry));
        }
    }

    [Test]
    public void Источник_секретов_объявляет_живой_токен_только_когда_он_задан()
    {
        var settings = new MemorySettings();
        var source = new SpaceSnoopSecretSource(settings);

        var empty = source.Collect();

        settings.SetValue(SettingsKeys.McpToken, "  " + Token + "  ");

        var filled = source.Collect();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(empty.Values, Is.Empty);
            Assert.That(empty.KeyNames, Does.Contain("token"));
            Assert.That(
                filled.Values,
                Is.EqualTo(
                    new[]
                    {
                        Token,
                    }));
        }
    }

    [Test]
    public void Непрочитанные_настройки_не_роняют_вклад_и_называются_человеку()
    {
        var operations = new PerformanceOperations(TestDiagnostics.Monitor());
        var source = new SpaceSnoopBundleSource(new MemorySettings(), operations, Path.Combine(_root, "нет-такого.toml"));

        var bundle = Bundle(Secrets(null), source.Collect().ToList());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bundle.Entries.Select(static entry => entry.Name), Does.Not.Contain(SpaceSnoopBundleSource.SettingsEntry));
            Assert.That(bundle.Entries.Select(static entry => entry.Name), Does.Contain(SpaceSnoopBundleSource.OperationsEntry));
            Assert.That(
                Text(bundle.Entries, SpaceSnoopBundleSource.SettingsFailureEntry),
                Does.Contain("Настройки приложения в пакет не попали").And.Contain("Файла настроек ещё нет"));

            Assert.That(bundle.Describe(), Does.Contain(SpaceSnoopBundleSource.SettingsFailureEntry));
        }
    }

    [TestCase("source", "target")]
    [TestCase("источник", "цель")]
    public void Пакет_скрывает_пути_профиля_и_сохраняет_читаемые_TOML_и_JSON(string leftLeaf, string rightLeaf)
    {
        var path = Path.Combine(_root, TomlSettingsFile.PrimaryFileName);
        var table = new TomlTable();
        var profiles = JsonSerializer.Serialize(
            new[]
            {
                new
                {
                    Left = @"C:\private\work\" + leftLeaf,
                    Right = @"\\server\share\private\" + rightLeaf,
                    Enabled = false,
                },
            });

        TomlSettingsFile.SetString(table, "wpf.schedule.profiles", profiles);
        TomlSettingsFile.SetString(table, "wpf.mcp.token", Token);
        TomlSettingsFile.Save(path, table);
        using var monitor = TestDiagnostics.Monitor();
        var source = new SpaceSnoopBundleSource(new MemorySettings(), new(monitor), path);
        var payload = new DiagnosticsPayload(
            MachineProfile.Capture(new()),
            PerformanceSnapshot.Empty,
            PerformanceHistory.Empty,
            PerformanceHitches.Empty,
            "сводка",
            [],
            source.Collect().ToList());

        var bundle = DiagnosticsBundle.Build(payload, Secrets(Token), new());
        var text = Text(bundle.Entries, SpaceSnoopBundleSource.SettingsEntry);
        var exported = Path.Combine(_root, "exported.toml");
        File.WriteAllText(exported, text);
        var restored = TomlSettingsFile.GetString(TomlSettingsFile.Read(exported), "wpf.schedule.profiles");
        using var parsed = JsonDocument.Parse(restored!);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Not.Contain("private").And.Not.Contain(Token));
            Assert.That(parsed.RootElement[0].GetProperty("Left").GetString(), Is.EqualTo(@"C:\…\" + leftLeaf));
            Assert.That(parsed.RootElement[0].GetProperty("Right").GetString(), Is.EqualTo(@"\\server\share\…\" + rightLeaf));
            Assert.That(parsed.RootElement[0].GetProperty("Enabled").GetBoolean(), Is.False);
        }
    }

    [Test]
    public void Пакет_сохраняет_неполноту_Mft_и_фактические_потоки_движка()
    {
        File.WriteAllBytes(Path.Combine(_root, "sample.bin"), new byte[128]);
        var tree = new DiskSpaceCalculator().Calculate(new(_root));
        var summary = new ScanSummaryViewModel();
        using var monitor = TestDiagnostics.Monitor();
        var operations = new PerformanceOperations(monitor);

        var mftRun = summary.Apply(
            tree,
            TimeSpan.FromSeconds(2),
            new(100, 0, 16),
            notes: new(true, 8, 0, 1, 55, 0, 0));

        operations.ReportRun(mftRun);

        var source = new SpaceSnoopBundleSource(new MemorySettings(), operations, Path.Combine(_root, "missing.toml"));
        var entries = Build(Secrets(null), [], source.Collect().ToList());
        using var parsed = JsonDocument.Parse(Text(entries, SpaceSnoopBundleSource.OperationsEntry));
        var lastRun = parsed.RootElement.GetProperty("lastRun");
        var notes = lastRun.GetProperty("notes");

        var walkRun = summary.Apply(
            tree,
            TimeSpan.FromSeconds(2),
            new(100, 0, 16),
            notes: ScanNotes.ForTraversal(16));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lastRun.GetProperty("traversal").GetProperty("parallelism").GetInt32(), Is.EqualTo(8));
            Assert.That(notes.GetProperty("engine").GetString(), Is.EqualTo("mft"));
            Assert.That(notes.GetProperty("droppedObjects").GetInt64(), Is.EqualTo(1));
            Assert.That(notes.GetProperty("droppedBytes").GetInt64(), Is.EqualTo(55));
            Assert.That(walkRun.Traversal?.Parallelism, Is.EqualTo(16));
        }
    }

    [TestCase(Token)]
    [TestCase(@"synthetic\token")]
    [TestCase("synthetic\"token")]
    public void Пакет_сохраняет_частичную_очистку_и_вымарывает_секрет_из_её_ошибки(string secret)
    {
        using var monitor = TestDiagnostics.Monitor();
        var operations = new PerformanceOperations(monitor);
        operations.ReportCleanup(new(DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow, 2, 3, 1024, true, null, [$"занят {secret}"]));
        var source = new SpaceSnoopBundleSource(new MemorySettings(), operations, Path.Combine(_root, "missing.toml"));
        var entries = Build(Secrets(secret), [], source.Collect().ToList());
        var text = Text(entries, SpaceSnoopBundleSource.OperationsEntry);
        using var parsed = JsonDocument.Parse(text);
        var cleanup = parsed.RootElement.GetProperty("lastCleanup");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cleanup.GetProperty("deleted").GetInt32(), Is.EqualTo(2));
            Assert.That(cleanup.GetProperty("skipped").GetInt32(), Is.EqualTo(3));
            Assert.That(cleanup.GetProperty("cancelled").GetBoolean(), Is.True);
            Assert.That(cleanup.GetProperty("reportedErrors")[0].GetString(), Does.Not.Contain(secret).And.Contain(DiagnosticsSecrets.Mask));
        }
    }

    [Test]
    public void Пакеты_одного_процесса_содержат_стабильный_сеанс_и_версию_сборки()
    {
        using var monitor = TestDiagnostics.Monitor();
        var source = new SpaceSnoopBundleSource(new MemorySettings(), new(monitor), Path.Combine(_root, "missing.toml"));
        using var first = JsonDocument.Parse(Text(source.Collect().ToList(), SpaceSnoopBundleSource.SessionEntry));
        using var second = JsonDocument.Parse(Text(source.Collect().ToList(), SpaceSnoopBundleSource.SessionEntry));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.RootElement.GetProperty("sessionId").GetString(), Is.EqualTo(second.RootElement.GetProperty("sessionId").GetString()));
            Assert.That(first.RootElement.GetProperty("sessionId").GetString(), Is.Not.Empty);
            Assert.That(first.RootElement.GetProperty("processId").GetInt32(), Is.EqualTo(Environment.ProcessId));
            Assert.That(first.RootElement.GetProperty("buildVersion").GetString(), Is.EqualTo(AppInfo.InformationalVersion));
            Assert.That(first.RootElement.GetProperty("processStartedAtUtc").GetDateTime(), Is.LessThanOrEqualTo(first.RootElement.GetProperty("capturedAtUtc").GetDateTime()));
        }
    }

    [Test]
    public void Пара_имя_значение_глушится_и_без_объявленного_токена()
    {
        var entries = Build(Secrets(null), [new("wpf-20260920.log", "2026-09-20 18:00:11.234 +03:00 [INF] Запрос token=eyJhbGciOiJIUzI1 завершился")]);

        Assert.That(
            Text(entries, "logs/wpf-20260920.log"),
            Is.EqualTo($"2026-09-20 18:00:11.234 +03:00 [INF] Запрос token={DiagnosticsSecrets.Mask} завершился"));
    }

    [Test]
    public void Прикладной_вклад_несёт_настройки_и_прогон()
    {
        var settings = new MemorySettings();
        var path = Path.Combine(_root, TomlSettingsFile.PrimaryFileName);
        File.WriteAllText(path, $"[wpf.mcp]{Environment.NewLine}token = \"{Token}\"");

        var operations = new PerformanceOperations(TestDiagnostics.Monitor());
        operations.ReportRun(new("Сканирование", 120, 4096, TimeSpan.FromSeconds(3)));

        var entries = Build(Secrets(Token), [], new SpaceSnoopBundleSource(settings, operations, path).Collect().ToList());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                entries.Select(static entry => entry.Name),
                Does.Contain(SpaceSnoopBundleSource.SettingsEntry).And.Contain(SpaceSnoopBundleSource.OperationsEntry));

            Assert.That(Text(entries, SpaceSnoopBundleSource.SettingsEntry), Does.Not.Contain(Token));
            Assert.That(Text(entries, SpaceSnoopBundleSource.OperationsEntry), Does.Contain("Сканирование"));
        }
    }

    private static DiagnosticsSecrets Secrets(string? token)
    {
        var settings = new MemorySettings();

        if (token is not null)
        {
            settings.SetValue(SettingsKeys.McpToken, token);
        }

        return new(DiagnosticsSecretRules.Merge([DiagnosticsSecretRules.Common, new SpaceSnoopSecretSource(settings).Collect()]));
    }

    private static IReadOnlyList<DiagnosticsEntry> Build(
        DiagnosticsSecrets secrets,
        IReadOnlyList<DiagnosticsEntry> logs,
        IReadOnlyList<DiagnosticsEntry>? contributed = null)
    {
        var payload = new DiagnosticsPayload(
            MachineProfile.Capture(new()),
            PerformanceSnapshot.Empty,
            PerformanceHistory.Empty,
            PerformanceHitches.Empty,
            "сводка",
            logs,
            contributed ?? []);

        return DiagnosticsBundle.Build(payload, secrets, null).Entries;
    }

    private static DiagnosticsBundle Bundle(DiagnosticsSecrets secrets, IReadOnlyList<DiagnosticsEntry> contributed)
    {
        var payload = new DiagnosticsPayload(
            MachineProfile.Capture(new()),
            PerformanceSnapshot.Empty,
            PerformanceHistory.Empty,
            PerformanceHitches.Empty,
            "сводка",
            [],
            contributed);

        return DiagnosticsBundle.Build(payload, secrets, null);
    }

    private static string Text(IReadOnlyList<DiagnosticsEntry> entries, string name)
    {
        return entries.Single(entry => entry.Name == name).Text;
    }
}
