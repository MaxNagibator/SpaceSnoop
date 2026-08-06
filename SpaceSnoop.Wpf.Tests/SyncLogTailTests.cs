using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncLogTailTests
{
    private string _dir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "SpaceSnoop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Test]
    public void Хвост_по_ротированным_файлам_новейшие_первыми_с_лимитом()
    {
        File.WriteAllLines(Path.Combine(_dir, "sync-log.txt"), ["a", "b"]);
        File.WriteAllLines(Path.Combine(_dir, "sync-log_001.txt"), ["c", "d"]);
        File.WriteAllLines(Path.Combine(_dir, "sync-log_002.txt"), ["e", "f"]);

        var tail = SyncLog.ReadTail(_dir, static _ => true, 3);

        Assert.That(tail, Is.EqualTo(new[] { "f", "e", "d" }));
    }

    [Test]
    public void Журнал_легаси_приложения_в_хвост_не_попадает()
    {
        File.WriteAllLines(Path.Combine(_dir, "sync-log.txt"), ["[x] Синхронизация: 1 успешно, 0 ошибок"]);
        File.WriteAllLines(Path.Combine(_dir, "winforms-sync-log.txt"), ["[y] Синхронизация: 9 успешно, 0 ошибок"]);

        var tail = SyncLog.ReadTail(_dir, static _ => true, 10);

        Assert.That(tail, Is.EqualTo(new[] { "[x] Синхронизация: 1 успешно, 0 ошибок" }));
    }

    [Test]
    public void Предикат_отбирает_только_подходящие_строки()
    {
        File.WriteAllLines(Path.Combine(_dir, "sync-log.txt"), ["[x] Автосинхронизация", "  деталь", "[y] Синхронизация"]);

        var tail = SyncLog.ReadTail(_dir, static line => line.Contains("Автосинхронизация"), 40);

        Assert.That(tail, Is.EqualTo(new[] { "[x] Автосинхронизация" }));
    }

    [Test]
    public void Пустой_каталог_даёт_пустой_хвост()
    {
        Assert.That(SyncLog.ReadTail(_dir, static _ => true, 40), Is.Empty);
    }

    [Test]
    public void Append_пишет_заголовок_первой_строкой_и_не_ломается_на_скобках_в_пути()
    {
        var path = Path.Combine(_dir, "sync-log.txt");
        var report = new SyncReport();
        report.Applied.Add(new(SyncAction.CopyToRight, "dir{0}/file}.txt", 4));

        SyncLog.Append(path, "[2026-06-30 12:00:00] Автосинхронизация [test]: 1 успешно, 0 ошибок", report);

        var lines = File.ReadAllLines(path);

        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Does.StartWith("[").And.Contains("Автосинхронизация"));
            Assert.That(lines, Has.Some.Contains("dir{0}/file}.txt"));
        });
    }
}
