using ModelContextProtocol;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Export;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.Mcp;

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
        Assert.That(McpBridge.ClampEntryLimit(requested), Is.EqualTo(expected));
    }

    [Test]
    public void Дефолтный_лимит_экспорта_проходит_кламп_без_изменений()
    {
        Assert.That(McpBridge.ClampEntryLimit(ComparisonExport.DefaultEntryLimit), Is.EqualTo(ComparisonExport.DefaultEntryLimit));
    }

    [TestCase(0, ScanExport.MinDepth)]
    [TestCase(-3, ScanExport.MinDepth)]
    [TestCase(3, 3)]
    [TestCase(int.MaxValue, ScanExport.MaxDepth)]
    public void Глубина_скана_от_агента_зажимается_в_диапазон(int requested, int expected)
    {
        Assert.That(McpBridge.ClampDepth(requested), Is.EqualTo(expected));
    }

    [Test]
    public void Дефолтная_глубина_скана_проходит_кламп_без_изменений()
    {
        Assert.That(McpBridge.ClampDepth(ScanExport.DefaultDepth), Is.EqualTo(ScanExport.DefaultDepth));
    }

    [TestCase(0, 1)]
    [TestCase(-5, 1)]
    [TestCase(60, 60)]
    [TestCase(int.MaxValue, AppDefaults.PerformanceHistoryPointsMax)]
    public void Число_точек_истории_зажимается_в_диапазон(int requested, int expected)
    {
        Assert.That(McpBridge.ClampHistoryPoints(requested), Is.EqualTo(expected));
    }

    [Test]
    public void До_первого_замера_окно_наблюдения_не_обещает_секунд()
    {
        Assert.That(McpBridge.DescribeWindow(PerformanceSnapshot.Empty), Is.EqualTo("замеров ещё нет"));
    }

    [Test]
    public void Окно_наблюдения_называет_охваченное_время_и_число_замеров()
    {
        var snapshot = PerformanceSnapshot.Empty with { SampleCount = 20, ObservedSpanSeconds = 13.5 };

        Assert.That(McpBridge.DescribeWindow(snapshot), Does.StartWith("последние 13").And.EndWith("с (20 замеров)"));
    }

    [Test]
    public void Путь_скана_пропускается_для_существующего_каталога()
    {
        Assert.DoesNotThrow(() => McpBridge.ValidateScanPath(_left));
    }

    [Test]
    public void Пустой_путь_скана_отбивается()
    {
        Assert.That(
            Assert.Throws<McpException>(() => McpBridge.ValidateScanPath(string.Empty))?.Message,
            Does.Contain("должен быть задан"));
    }

    [Test]
    public void Несуществующий_путь_скана_отбивается()
    {
        var missing = Path.Combine(_root, "нет-такого");

        Assert.That(
            Assert.Throws<McpException>(() => McpBridge.ValidateScanPath(missing))?.Message,
            Does.Contain("не найден"));
    }

    [Test]
    public void Валидация_пропускает_разные_существующие_каталоги()
    {
        Assert.DoesNotThrow(() => McpBridge.Validate(_left, _right, SyncMode.LeftToRight));
    }

    [TestCase("", "C:\\")]
    [TestCase("C:\\", "")]
    public void Пустой_путь_отбивается(string left, string right)
    {
        Assert.That(
            Assert.Throws<McpException>(() => McpBridge.Validate(left, right, SyncMode.LeftToRight))?.Message,
            Does.Contain("должны быть заданы"));
    }

    [Test]
    public void Вложенные_каталоги_отбиваются()
    {
        var inner = Path.Combine(_left, "inner");
        Directory.CreateDirectory(inner);

        Assert.That(
            Assert.Throws<McpException>(() => McpBridge.Validate(_left, inner, SyncMode.LeftToRight))?.Message,
            Does.Contain("вложены"));
    }

    [Test]
    public void Недоступный_источник_отбивается()
    {
        var missing = Path.Combine(_root, "нет-такого");

        Assert.That(
            Assert.Throws<McpException>(() => McpBridge.Validate(missing, _right, SyncMode.LeftToRight))?.Message,
            Does.Contain("источник недоступен"));
    }

    [TestCase(SyncMode.LeftToRight)]
    [TestCase(SyncMode.RightToLeft)]
    [TestCase(SyncMode.Bidirectional)]
    public void Индекс_режима_обратим_маппингу_профиля(SyncMode mode)
    {
        Assert.That(HeadlessSync.MapMode(SyncProfile.IndexOfMode(mode)), Is.EqualTo(mode));
    }
}
