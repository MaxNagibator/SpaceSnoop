using SpaceSnoop.Core;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanSummaryTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-summary-" + Guid.NewGuid().ToString("N"));
        var nested = Directory.CreateDirectory(Path.Combine(_root, "nested"));

        File.WriteAllBytes(Path.Combine(_root, "own.bin"), new byte[1024]);
        File.WriteAllBytes(Path.Combine(nested.FullName, "deep.bin"), new byte[4096]);
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
    public void Новый_скан_без_пропусков_убирает_строку_прошлого()
    {
        var tree = new DiskSpaceCalculator().Calculate(new(_root));
        var summary = new ScanSummaryViewModel(false, () => Task.CompletedTask);

        summary.Apply(tree, TimeSpan.FromSeconds(1), new(10, 4, 1));
        summary.Refresh(tree);
        var afterRefresh = summary.HasUnreadNote;
        summary.Apply(tree, TimeSpan.FromSeconds(1), new(10, 0, 1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterRefresh, Is.True);
            Assert.That(summary.HasUnreadNote, Is.False);
        }
    }

    [Test]
    public void Объём_прогона_считается_по_всему_дереву_а_не_по_файлам_корня()
    {
        var tree = new DiskSpaceCalculator().Calculate(new(_root));
        var summary = new ScanSummaryViewModel();

        var run = summary.Apply(tree, TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(run.Bytes, Is.EqualTo(5120));
            Assert.That(run.BytesPerSecond, Is.EqualTo(2560).Within(1));
        });
    }

    [Test]
    public void С_правами_администратора_строка_остаётся_а_кнопки_перезапуска_нет()
    {
        var tree = new DiskSpaceCalculator().Calculate(new(_root));
        var summary = new ScanSummaryViewModel(true, () => Task.CompletedTask);

        summary.Apply(tree, TimeSpan.FromSeconds(1), new(10, 4, 1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.HasUnreadNote, Is.True);
            Assert.That(summary.CanRestartAsAdmin, Is.False);
            Assert.That(summary.UnreadNoteHint, Does.Contain("даже с правами администратора"));
        }
    }

    [Test]
    public void Сводка_скана_передаёт_потоки_и_заметки_движка_в_операцию()
    {
        var tree = new DiskSpaceCalculator().Calculate(new(_root));
        var summary = new ScanSummaryViewModel();
        var notes = new ScanNotes(true, 1, 0, 3, 4096, 0, 0);

        var run = summary.Apply(tree, TimeSpan.FromSeconds(2), new(10, 0, 16), notes: notes);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(run.Traversal, Is.EqualTo(new PerformanceTraversal(10, 0, 1)));
            Assert.That(run.Notes, Is.EqualTo(notes));
        }
    }

    [Test]
    public void Скорость_итога_меряется_файлами_а_не_байтами_в_секунду()
    {
        var tree = new DiskSpaceCalculator().Calculate(new(_root));
        var summary = new ScanSummaryViewModel();

        summary.Apply(tree, TimeSpan.FromSeconds(2));

        Assert.That(summary.ResultRateText, Is.EqualTo("1 файл/с"));
    }

    [TestCase(0, "")]
    [TestCase(1, "1 каталог не прочитан")]
    [TestCase(3, "3 каталога не прочитано")]
    [TestCase(12, "12 каталогов не прочитано")]
    [TestCase(239, "239 каталогов не прочитано")]
    public void Строка_непрочитанных_каталогов_склоняется_и_видна_только_при_пропусках(long unread, string expected)
    {
        var tree = new DiskSpaceCalculator().Calculate(new(_root));
        var summary = new ScanSummaryViewModel(false, () => Task.CompletedTask);

        summary.Apply(tree, TimeSpan.FromSeconds(1), new(10, unread, 1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.UnreadNote, Is.EqualTo(expected));
            Assert.That(summary.HasUnreadNote, Is.EqualTo(unread > 0));
            Assert.That(summary.CanRestartAsAdmin, Is.EqualTo(unread > 0));
        }
    }
}
