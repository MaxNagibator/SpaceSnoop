using ModelContextProtocol;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Export;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.Mcp;
using SpaceSnoop.Wpf.ViewModels.Sync;
using System.Text.Json;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class McpBridgeTests
{
    private string _root = string.Empty;
    private string _left = string.Empty;
    private string _right = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "ss_mcp_" + Guid.NewGuid().ToString("N"));
        _left = Path.Combine(_root, "left");
        _right = Path.Combine(_root, "right");
        Directory.CreateDirectory(_left);
        Directory.CreateDirectory(_right);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [TestCase(0, AppDefaults.McpEntryLimitMin)]
    [TestCase(-100, AppDefaults.McpEntryLimitMin)]
    [TestCase(500, 500)]
    [TestCase(int.MaxValue, AppDefaults.McpEntryLimitMax)]
    public void Лимит_записей_от_агента_зажимается_в_диапазон(int requested, int expected)
    {
        Assert.That(McpGuards.ClampEntryLimit(requested), Is.EqualTo(expected));
    }

    [Test]
    public void Дефолтный_лимит_экспорта_проходит_кламп_без_изменений()
    {
        Assert.That(McpGuards.ClampEntryLimit(ComparisonExport.DefaultEntryLimit), Is.EqualTo(ComparisonExport.DefaultEntryLimit));
    }

    [TestCase(0, ScanExport.MinDepth)]
    [TestCase(-3, ScanExport.MinDepth)]
    [TestCase(3, 3)]
    [TestCase(int.MaxValue, ScanExport.MaxDepth)]
    public void Глубина_скана_от_агента_зажимается_в_диапазон(int requested, int expected)
    {
        Assert.That(McpGuards.ClampDepth(requested), Is.EqualTo(expected));
    }

    [Test]
    public void Дефолтная_глубина_скана_проходит_кламп_без_изменений()
    {
        Assert.That(McpGuards.ClampDepth(ScanExport.DefaultDepth), Is.EqualTo(ScanExport.DefaultDepth));
    }

    [TestCase(0, 1)]
    [TestCase(-5, 1)]
    [TestCase(60, 60)]
    [TestCase(int.MaxValue, AppDefaults.PerformanceHistoryPointsMax)]
    public void Число_точек_истории_зажимается_в_диапазон(int requested, int expected)
    {
        Assert.That(McpGuards.ClampHistoryPoints(requested), Is.EqualTo(expected));
    }

    [Test]
    public void До_первого_замера_окно_наблюдения_не_обещает_секунд()
    {
        Assert.That(McpFormat.DescribeWindow(PerformanceSnapshot.Empty), Is.EqualTo("замеров ещё нет"));
    }

    [Test]
    public void Окно_наблюдения_называет_охваченное_время_и_число_замеров()
    {
        var snapshot = PerformanceSnapshot.Empty with { SampleCount = 20, ObservedSpanSeconds = 13.5 };

        Assert.That(McpFormat.DescribeWindow(snapshot), Does.StartWith("последние 13").And.Contains("(20 замеров)"));
    }

    [Test]
    public void Снимок_без_истории_не_несёт_поля_history()
    {
        var json = JsonDocument.Parse(McpFormat.Serialize(Performance(null))).RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(json.TryGetProperty("history", out _), Is.False);
            Assert.That(json.TryGetProperty("operation", out _), Is.False);
            Assert.That(json.GetProperty("sampleCount").GetInt32(), Is.EqualTo(20));
            Assert.That(json.GetProperty("startupSeconds").GetDouble(), Is.EqualTo(1.25));
        });
    }

    [Test]
    public void История_выходит_в_JSON_рядом_со_снимком()
    {
        var json = JsonDocument.Parse(McpFormat.Serialize(Performance(McpFormat.DescribeHistory(History(), 60, 240)))).RootElement;
        var history = json.GetProperty("history");
        var timeline = history.GetProperty("timeline");

        Assert.Multiple(() =>
        {
            Assert.That(history.GetProperty("points").GetInt32(), Is.EqualTo(2));
            Assert.That(history.GetProperty("folded").GetInt32(), Is.EqualTo(3));
            Assert.That(history.GetProperty("requestedSeconds").GetInt32(), Is.EqualTo(60));
            Assert.That(history.GetProperty("requestedPoints").GetInt32(), Is.EqualTo(240));
            Assert.That(history.GetProperty("gen0CollectionsInWindow").GetInt32(), Is.EqualTo(4));
            Assert.That(timeline[0].GetProperty("gen0CollectionsTotal").GetInt32(), Is.EqualTo(1));
            Assert.That(timeline.GetArrayLength(), Is.EqualTo(2));
            Assert.That(timeline[0].GetProperty("operation").GetString(), Is.EqualTo("Сканирование"));
            Assert.That(timeline[1].TryGetProperty("operation", out _), Is.False);
        });
    }

    [Test]
    public void Кадры_окна_выходят_в_JSON_отдельными_полями()
    {
        var json = JsonDocument.Parse(McpFormat.Serialize(Performance(null))).RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(json.GetProperty("frameLastMs").GetDouble(), Is.EqualTo(4.3));
            Assert.That(json.GetProperty("framePeakMs").GetDouble(), Is.EqualTo(21.5));
            Assert.That(json.GetProperty("frameAverageMs").GetDouble(), Is.EqualTo(7));
            Assert.That(json.GetProperty("frameCount").GetInt32(), Is.EqualTo(31));
            Assert.That(json.GetProperty("slowFrameCount").GetInt32(), Is.EqualTo(2));
        });
    }

    [Test]
    public void Накопительные_и_оконные_счётчики_GC_названы_по_разному()
    {
        var json = JsonDocument.Parse(McpFormat.Serialize(Performance(McpFormat.DescribeHistory(History(), 60, 240)))).RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(json.GetProperty("gen0CollectionsTotal").GetInt32(), Is.EqualTo(4));
            Assert.That(json.TryGetProperty("gen0Collections", out _), Is.False);
            Assert.That(json.GetProperty("history").TryGetProperty("gen0Collections", out _), Is.False);
            Assert.That(json.GetProperty("workingSetPeakBytes").GetInt64(), Is.EqualTo(4096));
        });
    }

    [TestCase(SyncVerifyState.None, "None", "выключена")]
    [TestCase(SyncVerifyState.Interrupted, "Interrupted", "прервана")]
    [TestCase(SyncVerifyState.Completed, "Completed", "до конца")]
    public void Итог_синхронизации_различает_три_исхода_проверки(SyncVerifyState state, string expected, string hint)
    {
        var json = JsonDocument.Parse(McpFormat.Serialize(SyncResult(state, 0))).RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(json.GetProperty("verify").GetString(), Is.EqualTo(expected));
            Assert.That(json.GetProperty("verifyHint").GetString(), Does.Contain(hint));
        });
    }

    [Test]
    public void Пройденная_проверка_при_ошибках_не_обещает_сходимости()
    {
        var json = JsonDocument.Parse(McpFormat.Serialize(SyncResult(SyncVerifyState.Completed, 3))).RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(json.GetProperty("verifyHint").GetString(), Does.Contain("не проверялись"));
            Assert.That(json.GetProperty("verifyHint").GetString(), Does.Not.Contain("каталоги сошлись"));
        });
    }

    [TestCase(0, 0)]
    [TestCase(-5, 0)]
    [TestCase(60, 60)]
    [TestCase(int.MaxValue, AppDefaults.PerformanceHistorySecondsMax)]
    public void Окно_истории_зажимается_в_диапазон(int requested, int expected)
    {
        Assert.That(McpGuards.ClampHistorySeconds(requested), Is.EqualTo(expected));
    }

    [Test]
    public void Возраст_снимка_виден_агенту_чтобы_отличить_протухший()
    {
        var fresh = PerformanceSnapshot.Empty with { CapturedAtUtc = DateTime.UtcNow.AddSeconds(-30) };

        Assert.Multiple(() =>
        {
            Assert.That(McpFormat.SnapshotAge(fresh).TotalSeconds, Is.EqualTo(30).Within(1));
            Assert.That(McpFormat.SnapshotAge(PerformanceSnapshot.Empty), Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void Возраст_и_задержка_точки_округляются_до_десятых()
    {
        var history = McpFormat.DescribeHistory(History(), 60, 240);

        Assert.Multiple(() =>
        {
            Assert.That(history.Timeline[0].AgeMs, Is.EqualTo(3000.6));
            Assert.That(history.Timeline[0].UiDelayMs, Is.EqualTo(12.3));
            Assert.That(history.SpanSeconds, Is.EqualTo(2.5));
        });
    }

    [Test]
    public void Путь_скана_пропускается_для_существующего_каталога()
    {
        Assert.DoesNotThrow(() => McpGuards.ValidateScanPath(_left));
    }

    [Test]
    public void Пустой_путь_скана_отбивается()
    {
        Assert.That(
            Assert.Throws<McpException>(() => McpGuards.ValidateScanPath(string.Empty))?.Message,
            Does.Contain("должен быть задан"));
    }

    [Test]
    public void Несуществующий_путь_скана_отбивается()
    {
        var missing = Path.Combine(_root, "нет-такого");

        Assert.That(
            Assert.Throws<McpException>(() => McpGuards.ValidateScanPath(missing))?.Message,
            Does.Contain("не найден"));
    }

    [Test]
    public void Валидация_пропускает_разные_существующие_каталоги()
    {
        Assert.DoesNotThrow(() => McpGuards.Validate(_left, _right, SyncMode.LeftToRight));
    }

    [TestCase("", "C:\\")]
    [TestCase("C:\\", "")]
    public void Пустой_путь_отбивается(string left, string right)
    {
        Assert.That(
            Assert.Throws<McpException>(() => McpGuards.Validate(left, right, SyncMode.LeftToRight))?.Message,
            Does.Contain("должны быть заданы"));
    }

    [Test]
    public void Вложенные_каталоги_отбиваются()
    {
        var inner = Path.Combine(_left, "inner");
        Directory.CreateDirectory(inner);

        Assert.That(
            Assert.Throws<McpException>(() => McpGuards.Validate(_left, inner, SyncMode.LeftToRight))?.Message,
            Does.Contain("вложены"));
    }

    [Test]
    public void Недоступный_источник_отбивается()
    {
        var missing = Path.Combine(_root, "нет-такого");

        Assert.That(
            Assert.Throws<McpException>(() => McpGuards.Validate(missing, _right, SyncMode.LeftToRight))?.Message,
            Does.Contain("источник недоступен"));
    }

    [TestCase(SyncMode.LeftToRight)]
    [TestCase(SyncMode.RightToLeft)]
    [TestCase(SyncMode.Bidirectional)]
    public void Индекс_режима_обратим_маппингу_профиля(SyncMode mode)
    {
        Assert.That(HeadlessSync.MapMode(SyncProfile.IndexOfMode(mode)), Is.EqualTo(mode));
    }

    private static McpSyncResult SyncResult(SyncVerifyState state, int errorCount)
    {
        return new(3,
            1,
            4,
            1.5,
            2048,
            "2 КБ",
            state,
            McpFormat.DescribeVerifyState(state, errorCount),
            errorCount,
            0,
            0,
            0,
            [],
            [],
            "Готово",
            new("C:\\left", "C:\\right", SyncMode.LeftToRight, SyncWinner.Newest, false, string.Empty, false, true, new Dictionary<string, int>()));
    }

    private static McpPerformance Performance(McpPerformanceHistory? history)
    {
        return new(true,
            DateTime.UnixEpoch,
            0,
            "последние 10,0 с (20 замеров)",
            20,
            10,
            3,
            40,
            5,
            512,
            "512 Б",
            1024,
            "1 КБ",
            4096,
            "4 КБ",
            4,
            1,
            0,
            1.25,
            4.3,
            21.5,
            7,
            31,
            2,
            null,
            history);
    }

    private static PerformanceHistory History()
    {
        return new(DateTime.UnixEpoch,
            2.5049,
            4,
            1,
            0,
            3,
            [
                new(3000.56, 12.34, 100, 200, 1, 0, 0, "Сканирование"),
                new(500.44, 0, 150, 250, 5, 1, 0, null),
            ]);
    }
}
