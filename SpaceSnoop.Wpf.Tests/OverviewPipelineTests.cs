using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Schedule;
using SpaceSnoop.Wpf.ViewModels.Overview;

namespace SpaceSnoop.Wpf.Tests;

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

    private string Make(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
