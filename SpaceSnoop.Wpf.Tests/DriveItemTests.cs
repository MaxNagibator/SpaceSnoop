using Microsoft.Extensions.Logging.Abstractions;

using SpaceSnoop.Core;
using SpaceSnoop.Wpf.ViewModels.Scan;

using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class DriveItemTests
{
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
        var path = Path.Combine(Path.GetTempPath(), "spacesnoop", "недавний");
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
            Assert.That(item.Title, Is.EqualTo($"…{Path.DirectorySeparatorChar}spacesnoop{Path.DirectorySeparatorChar}недавний"));
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
            Assert.That(catalog.RecentDirectories.Select(item => item.Path), Is.EqualTo(new[] { first, second }));
            Assert.That(catalog.Items.Count(item => string.Equals(item.Path, first, StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
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
}
