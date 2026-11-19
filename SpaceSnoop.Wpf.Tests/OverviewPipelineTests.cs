using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Schedule;
using SpaceSnoop.Wpf.ViewModels.Overview;

namespace SpaceSnoop.Wpf.Tests;

[NonParallelizable]
public class OverviewPipelineTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "ss_overview_" + Guid.NewGuid().ToString("N"));
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

    [TestCase(0, true, true, null)]
    [TestCase(0, true, false, null)]
    [TestCase(0, false, true, OverviewRunStatus.Unavailable)]
    [TestCase(1, false, true, null)]
    [TestCase(1, true, false, OverviewRunStatus.Unavailable)]
    [TestCase(2, true, false, null)]
    [TestCase(2, false, true, null)]
    [TestCase(2, false, false, OverviewRunStatus.Unavailable)]
    public void Необходимые_для_сравнения_каталоги_зависят_от_направления(
        int mode,
        bool leftExists,
        bool rightExists,
        OverviewRunStatus? expected)
    {
        var profile = new SyncProfile
        {
            Left = leftExists ? Make("left") : Path.Combine(_root, "noleft"),
            Right = rightExists ? Make("right") : Path.Combine(_root, "noright"),
            Mode = mode,
        };

        Assert.That(OverviewPipeline.Classify(profile)?.Status, Is.EqualTo(expected));
    }

    [Test]
    public void Вложенные_каталоги_дают_Overlap()
    {
        var left = Make("left");
        var nested = Path.Combine(left, "inner");
        Directory.CreateDirectory(nested);
        var profile = new SyncProfile
        {
            Left = left,
            Right = nested,
        };

        Assert.That(OverviewPipeline.Classify(profile), Is.EqualTo(new OverviewPreflight(OverviewRunStatus.Overlap, SyncRoots.OverlapMessage)));
    }

    [Test]
    public async Task Отмена_во_время_предпроверки_последней_строки_отменяет_сравнение_всех()
    {
        var settings = new MemorySettings();
        SyncProfileStore.Save(
            settings,
            [
                new()
                {
                    Id = "one",
                    Name = "Один",
                    Left = Path.Combine(_root, "missing"),
                    Right = Make("right"),
                },
            ]);

        var overview = new OverviewViewModel(
            settings,
            new NoopDialogs(),
            new(new(), new(settings)),
            NullLogger<OverviewViewModel>.Instance,
            new(NullLogger<DirectoryComparer>.Instance),
            new(NullLogger<SyncEngine>.Instance),
            new FakeAppNavigator());

        var batch = overview.Batch;
        batch.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OverviewBatchViewModel.StatusCaption) && batch.StatusCaption.StartsWith("Пара 1 из 1", StringComparison.Ordinal))
            {
                batch.CancelOperationCommand.Execute(null);
            }
        };

        await batch.CompareAllCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(batch.StatusCaption, Is.EqualTo("Сравнение отменено."));
            Assert.That(overview.Rows.Rows.Single().Status, Is.EqualTo(OverviewRunStatus.None));
        }
    }

    [Test]
    public void Пустой_обзор_ведёт_создавать_профиль_на_страницу_расписания()
    {
        var settings = new MemorySettings();
        var navigator = new FakeAppNavigator();

        var overview = new OverviewViewModel(
            settings,
            new NoopDialogs(),
            new(new(), new(settings)),
            NullLogger<OverviewViewModel>.Instance,
            new(NullLogger<DirectoryComparer>.Instance),
            new(NullLogger<SyncEngine>.Instance),
            navigator);

        overview.CreateProfileCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overview.Rows.HasRows, Is.False);
            Assert.That(navigator.LastSection, Is.EqualTo(SectionKey.Schedule));
        }
    }

    [Test]
    public void Пустой_путь_даёт_Unavailable()
    {
        var profile = new SyncProfile
        {
            Left = string.Empty,
            Right = Make("right"),
        };

        Assert.That(OverviewPipeline.Classify(profile)?.Status, Is.EqualTo(OverviewRunStatus.Unavailable));
    }

    [Test]
    public async Task Синхронизация_зеркала_проверяет_пустоту_источника_вне_потока_интерфейса()
    {
        var left = Make("left");
        var right = Make("right");
        var settings = new MemorySettings();
        SyncProfileStore.Save(
            settings,
            [
                new()
                {
                    Id = "mirror",
                    Name = "Зеркало",
                    Left = left,
                    Right = right,
                    Mode = 0,
                    Mirror = true,
                },
            ]);

        var overview = new OverviewViewModel(
            settings,
            new NoopDialogs(showResult: true),
            new(new(), new(settings)),
            NullLogger<OverviewViewModel>.Instance,
            new(NullLogger<DirectoryComparer>.Instance),
            new(NullLogger<SyncEngine>.Instance),
            new FakeAppNavigator());

        var previous = OverviewPipeline.DirectoryEmpty;
        var callerThread = Environment.CurrentManagedThreadId;
        var probeThread = 0;

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        OverviewPipeline.DirectoryEmpty = _ =>
        {
            Interlocked.Exchange(ref probeThread, Environment.CurrentManagedThreadId);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return true;
        };

        try
        {
            var row = overview.Rows.Rows.Single();
            var run = overview.Batch.SyncRowCommand.ExecuteAsync(row);

            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(Volatile.Read(ref probeThread), Is.Not.EqualTo(callerThread));

            release.Set();
            await run.WaitAsync(TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(row.Status, Is.EqualTo(OverviewRunStatus.Error));
                Assert.That(row.Error, Is.EqualTo("Зеркало отменено: источник пуст."));
            }
        }
        finally
        {
            release.Set();
            OverviewPipeline.DirectoryEmpty = previous;
        }
    }

    [Test]
    public void Классификация_для_синхронизации_отличает_пустой_и_непустой_источник_зеркала()
    {
        var left = Make("left");
        var right = Make("right");
        var profile = new SyncProfile
        {
            Left = left,
            Right = right,
            Mode = 0,
            Mirror = true,
        };

        Assert.That(OverviewPipeline.ClassifyForSync(profile), Is.EqualTo(new OverviewPreflight(OverviewRunStatus.Error, "Зеркало отменено: источник пуст.")));

        File.WriteAllText(Path.Combine(left, "file.txt"), "data");

        Assert.That(OverviewPipeline.ClassifyForSync(profile), Is.Null);
        Assert.That(OverviewPipeline.Classify(profile), Is.Null);
    }

    [Test]
    public async Task Ошибка_проверки_пустоты_источника_переводит_строку_в_ошибку_без_срыва_пакета()
    {
        var leftBroken = Make("left-broken");
        var rightBroken = Make("right-broken");
        var leftValid = Make("left-valid");
        var rightValid = Make("right-valid");
        await File.WriteAllTextAsync(Path.Combine(leftValid, "note.txt"), "ok");

        var settings = new MemorySettings();
        SyncProfileStore.Save(
            settings,
            [
                new()
                {
                    Id = "broken",
                    Name = "Сломанный",
                    Left = leftBroken,
                    Right = rightBroken,
                    Mode = 0,
                    Mirror = true,
                },
                new()
                {
                    Id = "valid",
                    Name = "Живой",
                    Left = leftValid,
                    Right = rightValid,
                    Mode = 0,
                    Mirror = false,
                },
            ]);

        var overview = new OverviewViewModel(
            settings,
            new NoopDialogs(showResult: true),
            new(new(), new(settings)),
            NullLogger<OverviewViewModel>.Instance,
            new(NullLogger<DirectoryComparer>.Instance),
            new(NullLogger<SyncEngine>.Instance),
            new FakeAppNavigator());

        var previousEmpty = OverviewPipeline.DirectoryEmpty;
        var previousLog = OverviewBatchViewModel.AppendLog;
        OverviewPipeline.DirectoryEmpty = path =>
            string.Equals(path, leftBroken, StringComparison.OrdinalIgnoreCase)
                ? throw new IOException("Сетевой путь недоступен")
                : false;
        OverviewBatchViewModel.AppendLog = static (_, _, _, _, _) => { };

        try
        {
            await overview.Batch.SyncAllCommand.ExecuteAsync(null);

            var brokenRow = overview.Rows.Rows[0];
            var validRow = overview.Rows.Rows[1];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(brokenRow.Status, Is.EqualTo(OverviewRunStatus.Error));
                Assert.That(brokenRow.Error, Is.EqualTo("Сетевой путь недоступен"));
                Assert.That(validRow.Status, Is.EqualTo(OverviewRunStatus.Synced));
            }
        }
        finally
        {
            OverviewPipeline.DirectoryEmpty = previousEmpty;
            OverviewBatchViewModel.AppendLog = previousLog;
        }
    }

    [Test]
    public async Task Запись_журнала_при_синхронизации_строки_обзора_выполняется_вне_потока_интерфейса()
    {
        var left = Make("left");
        var right = Make("right");
        await File.WriteAllTextAsync(Path.Combine(left, "item.txt"), "payload");

        var settings = new MemorySettings();
        SyncProfileStore.Save(
            settings,
            [
                new()
                {
                    Id = "one",
                    Name = "Один",
                    Left = left,
                    Right = right,
                    Mode = 0,
                },
            ]);

        var overview = new OverviewViewModel(
            settings,
            new NoopDialogs(showResult: true),
            new(new(), new(settings)),
            NullLogger<OverviewViewModel>.Instance,
            new(NullLogger<DirectoryComparer>.Instance),
            new(NullLogger<SyncEngine>.Instance),
            new FakeAppNavigator());

        var previousLog = OverviewBatchViewModel.AppendLog;
        var callerThread = Environment.CurrentManagedThreadId;
        var logThread = 0;

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        OverviewBatchViewModel.AppendLog = (_, _, _, _, _) =>
        {
            Interlocked.Exchange(ref logThread, Environment.CurrentManagedThreadId);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        try
        {
            var row = overview.Rows.Rows.Single();
            var run = overview.Batch.SyncRowCommand.ExecuteAsync(row);

            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(Volatile.Read(ref logThread), Is.Not.EqualTo(callerThread));

            release.Set();
            await run.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(row.Status, Is.EqualTo(OverviewRunStatus.Synced));
        }
        finally
        {
            release.Set();
            OverviewBatchViewModel.AppendLog = previousLog;
        }
    }

    private string Make(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
