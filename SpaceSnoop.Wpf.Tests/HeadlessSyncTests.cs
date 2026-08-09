using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class HeadlessSyncTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"spacesnoop-headless-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException exception)
        {
            TestContext.Out.WriteLine(exception.Message);
        }
    }

    [Test]
    public void Ошибки_важнее_расхождений_в_коде_возврата()
    {
        var clean = new SyncReport();
        var mismatched = new SyncReport();
        mismatched.Mismatches.Add(new("a.txt", SyncAction.CopyToRight, "приёмник не найден"));
        var failed = new SyncReport();
        failed.Errors.Add(new("a.txt", SyncAction.CopyToRight, "нет доступа"));
        failed.Mismatches.Add(new("b.txt", SyncAction.CopyToRight, "приёмник не найден"));

        Assert.Multiple(() =>
        {
            Assert.That(HeadlessSync.Outcome(clean), Is.EqualTo(0));
            Assert.That(HeadlessSync.Outcome(mismatched), Is.EqualTo(6));
            Assert.That(HeadlessSync.Outcome(failed), Is.EqualTo(1));
        });
    }

    [Test]
    public void Ненастроенные_каталоги_и_пересечение_разводятся_кодами()
    {
        var left = Directory.CreateDirectory(Path.Combine(_root, "left"));
        var nested = Path.Combine(left.FullName, "inner");
        Directory.CreateDirectory(nested);

        Assert.Multiple(() =>
        {
            Assert.That(Validate(string.Empty, string.Empty), Is.EqualTo(2));
            Assert.That(Validate(Path.Combine(_root, "нет-такого"), left.FullName), Is.EqualTo(3));
            Assert.That(Validate(left.FullName, nested), Is.EqualTo(5));
        });
    }

    [Test]
    public void Зеркало_с_пустым_источником_отменяется_отдельным_кодом()
    {
        var left = Directory.CreateDirectory(Path.Combine(_root, "left"));
        var right = Directory.CreateDirectory(Path.Combine(_root, "right"));

        var empty = Validate(left.FullName, right.FullName, mirror: true);
        File.WriteAllText(Path.Combine(left.FullName, "a.txt"), "данные");
        var filled = Validate(left.FullName, right.FullName, mirror: true);

        Assert.Multiple(() =>
        {
            Assert.That(empty, Is.EqualTo(4));
            Assert.That(filled, Is.EqualTo(0));
        });
    }

    [Test]
    public void Пути_из_настроек_приходят_обрезанными()
    {
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.SyncLeft, "  C:\\слева  ");
        settings.SetValue(SettingsKeys.SyncRight, "C:\\справа ");
        settings.SetValue(SettingsKeys.SyncMode, "1");

        var options = HeadlessSync.LoadOptions(settings, null, NullLogger.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(options!.Left, Is.EqualTo("C:\\слева"));
            Assert.That(options.Right, Is.EqualTo("C:\\справа"));
            Assert.That(options.Mode, Is.EqualTo(SyncMode.RightToLeft));
        });
    }

    [Test]
    public void Исключения_наследуются_от_общих_когда_свои_пусты()
    {
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.DefaultExclusions, "bin,obj");

        var inherited = HeadlessSync.LoadOptions(settings, null, NullLogger.Instance);
        settings.SetValue(SettingsKeys.SyncExclusions, ".git");
        var own = HeadlessSync.LoadOptions(settings, null, NullLogger.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(inherited!.Exclusions, Is.EqualTo("bin,obj"));
            Assert.That(own!.Exclusions, Is.EqualTo(".git"));
        });
    }

    [Test]
    public void Ненайденный_профиль_отменяет_прогон()
    {
        var options = HeadlessSync.LoadOptions(new MemorySettings(), "нет-такого", NullLogger.Instance);

        Assert.That(options, Is.Null);
    }

    private static int Validate(string left, string right, bool mirror = false)
    {
        var options = new HeadlessSync.RunOptions("тест", left, right, SyncMode.LeftToRight, mirror, SyncWinner.None, string.Empty);

        return HeadlessSync.Validate(options, NullLogger.Instance);
    }
}
