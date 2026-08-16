using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Mft;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Scan;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanRunnerTests
{
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopRunner_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        File.WriteAllBytes(Path.Combine(_tempDir, "данные.bin"), new byte[2048]);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private string _tempDir = null!;

    [Test]
    public void Выключенная_настройка_оставляет_обход_каталогами()
    {
        var outcome = Runner(mftEnabled: false).Run(new(_tempDir), 1, new(), CancellationToken.None);

        Assert.That(outcome.UsedMft, Is.False);
        Assert.That(outcome.ExtraNameBytes, Is.Zero);
        Assert.That(outcome.Root.TotalSize, Is.EqualTo(2048));
    }

    [Test]
    public void Обход_каталогами_остаётся_доступен_при_любом_параллелизме()
    {
        var single = Runner(mftEnabled: false).Run(new(_tempDir), 1, new(), CancellationToken.None);
        var parallel = Runner(mftEnabled: false).Run(new(_tempDir), 4, new(), CancellationToken.None);

        Assert.That(parallel.Root.TotalSize, Is.EqualTo(single.Root.TotalSize));
    }

    [TestCase(@"\\сервер\шара\каталог")]
    [TestCase(@"\\?\UNC\сервер\шара")]
    public void Сетевой_путь_движку_по_MFT_недоступен(string path)
    {
        Assert.That(MftScanner.Probe(path), Is.EqualTo(MftAvailability.NotFixedVolume));
    }

    private static ScanRunner Runner(bool mftEnabled)
    {
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.ScanMftEnabled, mftEnabled ? "true" : "false");

        return new(new(), new(), new ScanPreferences(settings), NullLogger<ScanRunner>.Instance);
    }
}
