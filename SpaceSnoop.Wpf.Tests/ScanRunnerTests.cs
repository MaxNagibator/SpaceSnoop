using KeepShell.Testing;
using Microsoft.Extensions.Logging;
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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Notes.UsedMft, Is.False);
            Assert.That(outcome.Notes.Engine, Is.EqualTo("directories"));
            Assert.That(outcome.Notes.ExtraNameBytes, Is.Zero);
            Assert.That(outcome.Notes.HasDrops, Is.False);
            Assert.That(outcome.Root.TotalSize, Is.EqualTo(2048));
        }
    }

    [Test]
    public void Обход_каталогами_докладывает_свой_параллелизм()
    {
        var outcome = Runner(mftEnabled: false).Run(new(_tempDir), 4, new(), CancellationToken.None);

        Assert.That(outcome.Notes.Parallelism, Is.EqualTo(4));
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

    [Test]
    public void Корень_тома_отличается_от_каталога_на_нём()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ScanRunner.IsVolumeRoot(new(Path.GetPathRoot(_tempDir)!)), Is.True);
            Assert.That(ScanRunner.IsVolumeRoot(new(_tempDir)), Is.False);
        }
    }

    [Test]
    public void Путь_к_корню_через_переход_вверх_остаётся_корнем()
    {
        var wandering = Path.Combine(Path.GetPathRoot(_tempDir)!, "каталог", "..");

        Assert.That(ScanRunner.IsVolumeRoot(new(wandering)), Is.True);
    }

    [Test]
    public void Настройка_только_для_диска_целиком_оставляет_каталог_обходу()
    {
        var journal = new EventSpy();

        var outcome = Runner(mftEnabled: true, mftRootOnly: true, journal).Run(new(_tempDir), 1, new(), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Notes.UsedMft, Is.False);
            Assert.That(outcome.Root.TotalSize, Is.EqualTo(2048));
            Assert.That(journal.Events, Does.Contain(1013));
        }
    }

    private static ScanRunner Runner(bool mftEnabled, bool mftRootOnly = false, ILogger<ScanRunner>? logger = null)
    {
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.ScanMftEnabled, mftEnabled ? "true" : "false");
        settings.SetValue(SettingsKeys.ScanMftRootOnly, mftRootOnly ? "true" : "false");

        return new(new(), new(), new ScanPreferences(settings), logger ?? NullLogger<ScanRunner>.Instance);
    }

    private sealed class EventSpy : ILogger<ScanRunner>
    {
        public List<int> Events { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Events.Add(eventId.Id);
        }
    }
}
