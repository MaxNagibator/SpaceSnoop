using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class DriveItemTests
{
    [Test]
    public void Выбранная_цель_помечена_и_среди_томов_и_среди_недавних_включая_добавленные_позже()
    {
        var catalog = new DriveCatalog(NullLogger.Instance);
        var first = Path.Combine(Path.GetTempPath(), "spacesnoop-выбор-первый");
        var second = Path.Combine(Path.GetTempPath(), "spacesnoop-выбор-второй");

        catalog.AddDrive(first);
        catalog.Select(second);
        catalog.AddDrive(second);

        Assert.Multiple(() =>
        {
            Assert.That(
                catalog.Items.Where(item => item.IsSelected).Select(item => item.Path),
                Is.EqualTo(
                    new[]
                    {
                        second,
                    }));

            Assert.That(catalog.Volumes.Any(item => item.IsSelected), Is.False);
        });

        catalog.Select(catalog.Volumes[0].Path);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.Volumes[0].IsSelected, Is.True);
            Assert.That(catalog.RecentDirectories.Any(item => item.IsSelected), Is.False);
        });
    }

    [Test]
    public async Task Готовый_том_отдаёт_долю_занятого_и_подпись_размера()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var drive = new DriveInfo(root);

        Assert.That(drive.IsReady, Is.True, "Том временного каталога обязан быть готов – иначе кейс нечего проверять.");

        var item = new DriveItem(root);

        await item.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(item.IsDirectory, Is.False);
            Assert.That(item.HasUsage, Is.True);
            Assert.That(item.UsedRatio, Is.GreaterThan(0).And.LessThanOrEqualTo(1));
            Assert.That(item.UsedBytes, Is.EqualTo(item.TotalSize - item.FreeSpace));
            Assert.That(item.UsageLevel, Is.Not.EqualTo(DriveUsageLevel.None));
            Assert.That(item.SizeCaption, Is.EqualTo($"занято {SizeFormatter.Format(item.UsedBytes)} из {SizeFormatter.Format(item.TotalSize)}"));
            Assert.That(item.Caption, Is.EqualTo(item.SizeCaption));
            Assert.That(item.AutomationName, Is.EqualTo($"Диск {item.Title}, {item.SizeCaption}"));
        });
    }

    [Test]
    public async Task Каталог_не_получает_ни_доли_ни_подписи_и_зовётся_хвостом_пути()
    {
        var path = Path.Combine(Path.GetTempPath(), "spacesnoop", "recent", "недавний");
        var item = new DriveItem(path);

        await item.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(item.IsDirectory, Is.True);
            Assert.That(item.HasUsage, Is.False);
            Assert.That(item.UsedRatio, Is.Zero);
            Assert.That(item.UsageLevel, Is.EqualTo(DriveUsageLevel.None));
            Assert.That(item.SizeCaption, Is.Null);
            Assert.That(item.Caption, Is.Null);
            Assert.That(item.IsMissingMedia, Is.False);
            Assert.That(item.TypeHint, Is.Null);
            Assert.That(
                item.Title,
                Is.EqualTo(
                    $"{Path.GetPathRoot(path)!.TrimEnd(Path.DirectorySeparatorChar)}{Path.DirectorySeparatorChar}…{Path.DirectorySeparatorChar}recent{Path.DirectorySeparatorChar}недавний"));
        });
    }

    [TestCase(@"C:\Users\admin\AppData\Local", @"C:\…\AppData\Local")]
    [TestCase(@"C:\Users\admin\AppData\Local\", @"C:\…\AppData\Local")]
    [TestCase(@"C:\Sources\TheVSAKeeper\SpaceSnoop\.git", @"C:\…\SpaceSnoop\.git")]
    [TestCase(@"C:\Users\admin\AppData", @"C:\Users\admin\AppData")]
    [TestCase(@"C:\Users\admin", @"C:\Users\admin")]
    [TestCase(@"D:\Фото", @"D:\Фото")]
    [TestCase(@"\\nas\share\backup\2026\photos\raw", @"\\nas\share\…\photos\raw")]
    [TestCase(@"\\nas\share\backup\2026\photos", @"\\nas\share\backup\2026\photos")]
    public void Каталог_сокращается_в_середине_и_сохраняет_корень_и_родителя(string path, string expected)
    {
        Assert.That(new DriveItem(path).Title, Is.EqualTo(expected));
    }

    [TestCase(@"C:\", "C:")]
    [TestCase(@"C:\Users\admin\AppData", "AppData")]
    [TestCase(@"C:\Users\admin\AppData\", "AppData")]
    [TestCase(@"\\nas\share\backup", "backup")]
    public void Короткое_имя_цели_для_кнопки_скана_берёт_том_или_последний_каталог(string path, string expected)
    {
        Assert.That(DriveItem.ShortName(path), Is.EqualTo(expected));
    }

    [Test]
    public async Task Неопрашиваемый_том_остаётся_живой_плиткой_без_подписи_и_доли()
    {
        var item = new DriveItem($@"\\spacesnoop-нет-такого-сервера\backup{Path.DirectorySeparatorChar}");

        await item.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(item.IsDirectory, Is.False);
            Assert.That(item.IsProbed, Is.True);
            Assert.That(item.IsKnownVolume, Is.False);
            Assert.That(item.IsMissingMedia, Is.False, "Неопрошенный том нельзя объявлять пустым приводом – плитка перестала бы выбираться.");
            Assert.That(item.HasUsage, Is.False);
            Assert.That(item.Caption, Is.Null);
            Assert.That(item.RatioCaption, Is.Null);
            Assert.That(item.UsageLevel, Is.EqualTo(DriveUsageLevel.None));
        });
    }

    [Test]
    public void Обновление_подключённого_тома_добавляет_новую_букву_и_не_дублирует_существующую()
    {
        var catalog = new DriveCatalog(NullLogger.Instance);
        var taken = DriveInfo.GetDrives().Select(drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        var free = Enumerable.Range('D', 'Z' - 'D' + 1).Select(code => (char)code).First(letter => !taken.Contains(letter));
        var root = $@"{free}:\";

        catalog.Select(root);
        catalog.RefreshVolume(root);
        var countAfterFirst = catalog.Volumes.Count;
        var added = catalog.Volumes.Single(volume => string.Equals(volume.Path, root, StringComparison.OrdinalIgnoreCase));

        catalog.RefreshVolume(root);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(added.IsSelected, Is.True);
            Assert.That(catalog.Volumes, Has.Count.EqualTo(countAfterFirst));
            Assert.That(catalog.Items.Count(item => string.Equals(item.Path, root, StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Опрос_томов_не_возвращается_в_контекст_вызывающего()
    {
        var catalog = new DriveCatalog(NullLogger.Instance);

        Assert.That(catalog.Volumes, Is.Not.Empty, "Без единого тома кейс нечего проверять.");

        var context = new CountingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            catalog.LoadDriveLabels();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        await Task.WhenAll(catalog.Items.Select(item => item.LoadAsync()));

        Assert.That(
            context.Posts,
            Is.Zero,
            "Опрос тома доигрывается в контексте вызывающего: у async STA-теста NUnit этот контекст закрывается вместе с тестом, и опоздавший Post роняет хост.");
    }

    [Test]
    public void Повторное_добавление_недавнего_поднимает_его_наверх_без_дубликата()
    {
        var catalog = new DriveCatalog(NullLogger.Instance);
        var first = Path.Combine(Path.GetTempPath(), "spacesnoop-первый");
        var second = Path.Combine(Path.GetTempPath(), "spacesnoop-второй");

        catalog.AddDrive(first);
        catalog.AddDrive(second);
        catalog.AddDrive(first);

        Assert.Multiple(() =>
        {
            Assert.That(
                catalog.RecentDirectories.Select(item => item.Path),
                Is.EqualTo(
                    new[]
                    {
                        first, second,
                    }));

            Assert.That(catalog.Items.Count(item => string.Equals(item.Path, first, StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
        });
    }

    [Test]
    public void Список_недавних_держит_потолок_и_теряет_самый_старый()
    {
        var catalog = new DriveCatalog(NullLogger.Instance);
        var paths = Enumerable.Range(0, 9)
            .Select(index => Path.Combine(Path.GetTempPath(), $"spacesnoop-недавний-{index}"))
            .ToList();

        foreach (var path in paths)
        {
            catalog.AddDrive(path);
        }

        Assert.Multiple(() =>
        {
            Assert.That(catalog.RecentDirectories, Has.Count.EqualTo(8));
            Assert.That(catalog.RecentDirectories[0].Path, Is.EqualTo(paths[^1]));
            Assert.That(catalog.HasDrive(paths[0]), Is.False, "Самый старый каталог обязан выпасть и из общего списка целей.");
            Assert.That(catalog.RecentDirectories.Select(item => item.Path), Does.Not.Contain(paths[0]));
        });
    }

    [Test]
    public async Task Том_без_носителя_говорит_об_этом_вместо_размера()
    {
        var used = DriveInfo.GetDrives().Select(static drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        var free = "ZYXWVU".FirstOrDefault(letter => !used.Contains(letter));

        Assert.That(free, Is.Not.EqualTo('\0'), "Не нашлось свободной буквы диска для кейса.");

        var item = new DriveItem($"{free}:{Path.DirectorySeparatorChar}");

        await item.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(item.IsDirectory, Is.False);
            Assert.That(item.IsReady, Is.False);
            Assert.That(item.IsMissingMedia, Is.True);
            Assert.That(item.HasUsage, Is.False);
            Assert.That(item.SizeCaption, Is.Null);
            Assert.That(item.Caption, Is.EqualTo("нет носителя"));
            Assert.That(item.Title, Is.EqualTo($"{free}:"));
        });
    }

    [Test]
    public void Удаление_каталога_из_каталога_целей_оставляет_запасным_первый_том()
    {
        var catalog = new DriveCatalog(NullLogger.Instance);
        var recent = Path.Combine(Path.GetTempPath(), "spacesnoop-недавний");

        Assert.That(catalog.Volumes, Is.Not.Empty, "На машине обязан быть хотя бы один том.");

        catalog.AddDrive(recent);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.RecentDirectories.Select(item => item.Path), Does.Contain(recent));
            Assert.That(catalog.Volumes.Select(item => item.Path), Does.Not.Contain(recent));
        });

        var removed = catalog.RemoveDrive(recent);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(catalog.HasDrive(recent), Is.False);
            Assert.That(catalog.RecentDirectories, Is.Empty);
            Assert.That(catalog.FallbackPath, Is.EqualTo(catalog.Volumes[0].Path));
            Assert.That(catalog.RemoveDrive(recent), Is.False);
        });
    }

    [TestCase(0.0, DriveUsageLevel.Normal)]
    [TestCase(0.7499, DriveUsageLevel.Normal)]
    [TestCase(0.75, DriveUsageLevel.Warning)]
    [TestCase(0.8999, DriveUsageLevel.Warning)]
    [TestCase(0.90, DriveUsageLevel.Critical)]
    [TestCase(1.0, DriveUsageLevel.Critical)]
    public void Уровень_занятости_меняется_на_порогах_75_и_90_процентов(double ratio, DriveUsageLevel expected)
    {
        Assert.That(DriveItem.LevelFor(ratio), Is.EqualTo(expected));
    }

    private sealed class CountingSynchronizationContext : SynchronizationContext
    {
        private int _posts;

        public int Posts => Volatile.Read(ref _posts);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _posts);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }
}
