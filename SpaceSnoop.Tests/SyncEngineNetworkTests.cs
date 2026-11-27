using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using System.Diagnostics;

namespace SpaceSnoop.Tests;

[TestFixture]
public class SyncEngineNetworkTests
{
    private readonly List<string> _links = [];

    private string _tempDir = null!;
    private string _leftDir = null!;
    private string _rightDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopNetSync_{Guid.NewGuid():N}");
        _leftDir = Directory.CreateDirectory(Path.Combine(_tempDir, "left")).FullName;
        _rightDir = Directory.CreateDirectory(Path.Combine(_tempDir, "right")).FullName;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var link in _links.Where(static x => Directory.Exists(x)))
        {
            Directory.Delete(link, false);
        }

        _links.Clear();

        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public void Без_корзины_и_с_разрешением_изменённый_файл_заменяется_атомарно_а_лишние_удаляются_безвозвратно()
    {
        var leftModified = Path.Combine(_leftDir, "doc.txt");
        var rightModified = Path.Combine(_rightDir, "doc.txt");
        var rightDoomedFile = Path.Combine(_rightDir, "extra.txt");
        var rightDoomedDir = Directory.CreateDirectory(Path.Combine(_rightDir, "old-dir")).FullName;
        File.WriteAllText(leftModified, "new-content");
        File.WriteAllText(rightModified, "old-content");
        File.WriteAllText(rightDoomedFile, "delete-me");
        File.WriteAllText(Path.Combine(rightDoomedDir, "inside.txt"), "nested");

        var root = new DirectoryComparison("root", string.Empty);
        root.Files.Add(
            new("doc.txt", "doc.txt")
            {
                Status = ComparisonStatus.Modified,
                Action = SyncAction.CopyToRight,
                LeftSize = 11,
                RightSize = 11,
            });
        root.Files.Add(
            new("extra.txt", "extra.txt")
            {
                Status = ComparisonStatus.RightOnly,
                Action = SyncAction.DeleteRight,
                RightSize = 9,
                RightModified = File.GetLastWriteTime(rightDoomedFile),
            });
        var oldDir = new DirectoryComparison("old-dir", "old-dir")
        {
            Status = ComparisonStatus.RightOnly,
            Action = SyncAction.DeleteRight,
        };
        oldDir.Files.Add(
            new("inside.txt", Path.Combine("old-dir", "inside.txt"))
            {
                Status = ComparisonStatus.RightOnly,
                Action = SyncAction.DeleteRight,
                RightSize = 6,
                RightModified = File.GetLastWriteTime(Path.Combine(rightDoomedDir, "inside.txt")),
            });
        root.SubDirectories.Add(oldDir);

        var comparison = new ComparisonResult(_leftDir, _rightDir, root);
        var engine = new SyncEngine(
            NullLogger<SyncEngine>.Instance,
            showDeleteUi: false,
            recycleOverwritten: true,
            allowPermanentNetworkDelete: true,
            supportsRecycleBin: static _ => false);

        var report = engine.Execute(comparison, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Errors, Is.Empty);
            Assert.That(report.CopiedCount, Is.EqualTo(1));
            Assert.That(report.DeletedCount, Is.EqualTo(2));
            Assert.That(File.ReadAllText(rightModified), Is.EqualTo("new-content"));
            Assert.That(File.Exists(rightDoomedFile), Is.False);
            Assert.That(Directory.Exists(rightDoomedDir), Is.False);
        }
    }

    [Test]
    public void Безвозвратное_удаление_каталога_отклоняется_при_вложенной_ссылке_и_не_удаляет_её_цель()
    {
        var externalTarget = Directory.CreateDirectory(Path.Combine(_tempDir, "external-target")).FullName;
        var externalFile = Path.Combine(externalTarget, "untouched.txt");
        File.WriteAllText(externalFile, "precious");

        var doomedDir = Directory.CreateDirectory(Path.Combine(_rightDir, "doomed")).FullName;
        File.WriteAllText(Path.Combine(doomedDir, "local.txt"), "bye");
        var innerLink = Path.Combine(doomedDir, "link-out");

        TryCreateJunction(innerLink, externalTarget);

        var doomedPlan = new DirectoryComparison("doomed", "doomed")
        {
            Status = ComparisonStatus.RightOnly,
            Action = SyncAction.DeleteRight,
        };
        doomedPlan.Files.Add(
            new("local.txt", Path.Combine("doomed", "local.txt"))
            {
                Status = ComparisonStatus.RightOnly,
                Action = SyncAction.DeleteRight,
                RightSize = 3,
                RightModified = File.GetLastWriteTime(Path.Combine(doomedDir, "local.txt")),
            });

        var root = new DirectoryComparison("root", string.Empty);
        root.SubDirectories.Add(doomedPlan);

        var comparison = new ComparisonResult(_leftDir, _rightDir, root);
        var engine = new SyncEngine(
            NullLogger<SyncEngine>.Instance,
            showDeleteUi: false,
            recycleOverwritten: false,
            allowPermanentNetworkDelete: true,
            supportsRecycleBin: static _ => false);

        var report = engine.Execute(comparison, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Errors, Has.Count.EqualTo(1));
            Assert.That(Directory.Exists(doomedDir), Is.True);
            Assert.That(Directory.Exists(externalTarget), Is.True);
            Assert.That(File.Exists(externalFile), Is.True);
        }
    }

    [Test]
    public void Без_корзины_и_без_разрешения_Execute_бросает_до_первого_копирования()
    {
        var leftFile = Path.Combine(_leftDir, "copy.txt");
        var rightCopy = Path.Combine(_rightDir, "copy.txt");
        var rightDelete = Path.Combine(_rightDir, "extra.txt");
        File.WriteAllText(leftFile, "payload");
        File.WriteAllText(rightDelete, "delete-me");

        var root = new DirectoryComparison("root", string.Empty);
        root.Files.Add(
            new("copy.txt", "copy.txt")
            {
                Status = ComparisonStatus.LeftOnly,
                Action = SyncAction.CopyToRight,
                LeftSize = 7,
            });
        root.Files.Add(
            new("extra.txt", "extra.txt")
            {
                Status = ComparisonStatus.RightOnly,
                Action = SyncAction.DeleteRight,
                RightSize = 9,
            });

        var comparison = new ComparisonResult(_leftDir, _rightDir, root);
        var engine = new SyncEngine(
            NullLogger<SyncEngine>.Instance,
            showDeleteUi: false,
            recycleOverwritten: false,
            allowPermanentNetworkDelete: false,
            supportsRecycleBin: static _ => false);

        Assert.Throws<RecycleBinUnavailableException>(() => engine.Execute(comparison, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(rightCopy), Is.False);
            Assert.That(File.Exists(rightDelete), Is.True);
        }
    }

    [Test]
    public void Потеря_доступа_к_корзине_после_предпроверки_не_теряет_отчёт_о_копировании()
    {
        var rightCopy = Path.Combine(_rightDir, "copy.txt");
        var rightDelete = Path.Combine(_rightDir, "extra.txt");
        File.WriteAllText(Path.Combine(_leftDir, "copy.txt"), "payload");
        File.WriteAllText(rightDelete, "delete-me");
        var comparer = new DirectoryComparer(new(string.Empty), NullLogger<DirectoryComparer>.Instance);
        var comparison = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);
        comparison.ApplyMode(SyncMode.LeftToRight, mirror: true);

        var calls = 0;
        var engine = new SyncEngine(
            NullLogger<SyncEngine>.Instance,
            showDeleteUi: false,
            recycleOverwritten: false,
            allowPermanentNetworkDelete: false,
            supportsRecycleBin: _ => ++calls == 1);
        var report = engine.Execute(comparison, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.CopiedCount, Is.EqualTo(1));
            Assert.That(report.DeletedCount, Is.Zero);
            Assert.That(report.Errors, Has.Count.EqualTo(1));
            Assert.That(File.ReadAllText(rightCopy), Is.EqualTo("payload"));
            Assert.That(File.ReadAllText(rightDelete), Is.EqualTo("delete-me"));
        }
    }

    [Test]
    public void Заблокированное_удаление_без_корзины_не_прерывает_остальной_план()
    {
        var leftFile = Path.Combine(_leftDir, "copy.txt");
        var rightCopy = Path.Combine(_rightDir, "copy.txt");
        var rightDelete = Path.Combine(_rightDir, "extra.txt");
        File.WriteAllText(leftFile, "payload");
        File.WriteAllText(rightDelete, "keep-me");

        var root = new DirectoryComparison("root", string.Empty)
        {
            LeftIncomplete = true,
        };
        root.Files.Add(
            new("copy.txt", "copy.txt")
            {
                Status = ComparisonStatus.LeftOnly,
                Action = SyncAction.CopyToRight,
                LeftSize = 7,
            });
        root.Files.Add(
            new("extra.txt", "extra.txt")
            {
                Status = ComparisonStatus.RightOnly,
                Action = SyncAction.DeleteRight,
                RightSize = 7,
                DeleteRightBlocked = true,
            });

        var comparison = new ComparisonResult(_leftDir, _rightDir, root);
        var engine = new SyncEngine(
            NullLogger<SyncEngine>.Instance,
            showDeleteUi: false,
            recycleOverwritten: false,
            allowPermanentNetworkDelete: false,
            supportsRecycleBin: static _ => false);

        var report = engine.Execute(comparison, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.CopiedCount, Is.EqualTo(1));
            Assert.That(report.Errors, Has.Count.EqualTo(1));
            Assert.That(File.Exists(rightCopy), Is.True);
            Assert.That(File.Exists(rightDelete), Is.True);
        }
    }

    [Test]
    public void Безвозвратное_удаление_каталога_отклоняется_если_появился_новый_файл_или_изменилось_действие()
    {
        var doomedDir = Directory.CreateDirectory(Path.Combine(_rightDir, "doomed")).FullName;
        var plannedFile = Path.Combine(doomedDir, "planned.txt");
        var surpriseFile = Path.Combine(doomedDir, "new.txt");
        File.WriteAllText(plannedFile, "ok");

        var doomedPlan = new DirectoryComparison("doomed", "doomed")
        {
            Status = ComparisonStatus.RightOnly,
            Action = SyncAction.DeleteRight,
        };
        doomedPlan.Files.Add(
            new("planned.txt", Path.Combine("doomed", "planned.txt"))
            {
                Status = ComparisonStatus.RightOnly,
                Action = SyncAction.DeleteRight,
                RightSize = 2,
                RightModified = File.GetLastWriteTime(plannedFile),
            });

        var root = new DirectoryComparison("root", string.Empty);
        root.SubDirectories.Add(doomedPlan);

        var comparison = new ComparisonResult(_leftDir, _rightDir, root);
        var engine = new SyncEngine(
            NullLogger<SyncEngine>.Instance,
            showDeleteUi: false,
            recycleOverwritten: false,
            allowPermanentNetworkDelete: true,
            supportsRecycleBin: static _ => false);

        File.WriteAllText(surpriseFile, "unexpected");
        var firstReport = engine.Execute(comparison, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstReport.Errors, Has.Count.EqualTo(1));
            Assert.That(firstReport.Errors[0].Message, Does.Contain("Каталог изменился"));
            Assert.That(File.Exists(surpriseFile), Is.True);
        }

        File.Delete(surpriseFile);
        doomedPlan.Files[0].Action = SyncAction.Skip;

        var secondReport = engine.Execute(comparison, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(secondReport.Errors, Has.Count.EqualTo(1));
            Assert.That(secondReport.Errors[0].Message, Does.Contain("Каталог изменился"));
            Assert.That(File.Exists(plannedFile), Is.True);
        }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Безвозвратное_удаление_сохраняет_файл_который_изменился_после_сравнения(bool nested, bool changeLength)
    {
        var target = nested ? Directory.CreateDirectory(Path.Combine(_rightDir, "extra")).FullName : _rightDir;
        var file = Path.Combine(target, "file.txt");
        File.WriteAllText(file, "old");
        var originalTime = File.GetLastWriteTime(file);
        var comparer = new DirectoryComparer(new(string.Empty), NullLogger<DirectoryComparer>.Instance);
        var comparison = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);
        comparison.ApplyMode(SyncMode.LeftToRight, mirror: true);

        var replacement = changeLength ? "longer" : "new";
        File.WriteAllText(file, replacement);
        File.SetLastWriteTime(file, changeLength ? originalTime : originalTime.AddMinutes(1));
        var engine = new SyncEngine(
            NullLogger<SyncEngine>.Instance,
            showDeleteUi: false,
            recycleOverwritten: false,
            allowPermanentNetworkDelete: true,
            supportsRecycleBin: static _ => false);
        var report = engine.Execute(comparison, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Errors, Has.Count.EqualTo(1));
            Assert.That(report.Errors[0].Message, Does.Contain("изменился после сравнения"));
            Assert.That(report.DeletedCount, Is.Zero);
            Assert.That(File.ReadAllText(file), Is.EqualTo(replacement));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Безвозвратное_удаление_снимает_ReadOnly_через_дескриптор_только_если_файл_не_изменился(bool tamperAfterCompare)
    {
        var file = Path.Combine(_rightDir, "ro.txt");
        File.WriteAllText(file, "same");
        var originalTime = File.GetLastWriteTime(file);
        File.SetAttributes(file, FileAttributes.ReadOnly);

        var comparer = new DirectoryComparer(new(string.Empty), NullLogger<DirectoryComparer>.Instance);
        var comparison = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);
        comparison.ApplyMode(SyncMode.LeftToRight, mirror: true);

        if (tamperAfterCompare)
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.WriteAllText(file, "changed-length");
            File.SetLastWriteTime(file, originalTime);
            File.SetAttributes(file, FileAttributes.ReadOnly);
        }

        var engine = new SyncEngine(
            NullLogger<SyncEngine>.Instance,
            showDeleteUi: false,
            recycleOverwritten: false,
            allowPermanentNetworkDelete: true,
            supportsRecycleBin: static _ => false);

        try
        {
            var report = engine.Execute(comparison, CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                if (tamperAfterCompare)
                {
                    Assert.That(report.Errors, Has.Count.EqualTo(1));
                    Assert.That(report.DeletedCount, Is.Zero);
                    Assert.That(File.Exists(file), Is.True);
                    Assert.That(File.GetAttributes(file) & FileAttributes.ReadOnly, Is.EqualTo(FileAttributes.ReadOnly));
                }
                else
                {
                    Assert.That(report.Errors, Is.Empty);
                    Assert.That(report.DeletedCount, Is.EqualTo(1));
                    Assert.That(File.Exists(file), Is.False);
                }
            }
        }
        finally
        {
            if (File.Exists(file))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
        }
    }

    [Test]
    public void Безвозвратное_удаление_отказывает_для_занятого_файла_из_за_эксклюзивного_дескриптора()
    {
        var file = Path.Combine(_rightDir, "locked.txt");
        File.WriteAllText(file, "busy");
        var comparer = new DirectoryComparer(new(string.Empty), NullLogger<DirectoryComparer>.Instance);
        var comparison = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);
        comparison.ApplyMode(SyncMode.LeftToRight, mirror: true);

        var engine = new SyncEngine(
            NullLogger<SyncEngine>.Instance,
            showDeleteUi: false,
            recycleOverwritten: false,
            allowPermanentNetworkDelete: true,
            supportsRecycleBin: static _ => false);

        SyncReport report;
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            report = engine.Execute(comparison, CancellationToken.None);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Errors, Has.Count.EqualTo(1));
            Assert.That(report.DeletedCount, Is.Zero);
            Assert.That(File.Exists(file), Is.True);
        }
    }

    [Test]
    public void Отмена_при_безвозвратном_удалении_поддерева_останавливается_между_файлами_и_сохраняет_частичный_отчёт()
    {
        var doomedDir = Directory.CreateDirectory(Path.Combine(_rightDir, "extra")).FullName;
        var files = new[]
        {
            Path.Combine(doomedDir, "01.txt"),
            Path.Combine(doomedDir, "02.txt"),
            Path.Combine(doomedDir, "03.txt"),
        };

        foreach (var file in files)
        {
            File.WriteAllText(file, "payload");
        }

        var comparer = new DirectoryComparer(new(string.Empty), NullLogger<DirectoryComparer>.Instance);
        var comparison = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);
        comparison.ApplyMode(SyncMode.LeftToRight, mirror: true);

        var engine = new SyncEngine(
            NullLogger<SyncEngine>.Instance,
            showDeleteUi: false,
            recycleOverwritten: false,
            allowPermanentNetworkDelete: true,
            supportsRecycleBin: static _ => false);

        var report = new SyncReport();
        using var cts = new CancellationTokenSource();
        var progress = new InlineProgress(_ => cts.Cancel());

        Assert.Throws<OperationCanceledException>(() => engine.Execute(comparison, report, cts.Token, progress));

        var remaining = Directory.GetFiles(doomedDir);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Errors, Is.Empty);
            Assert.That(report.DeletedCount, Is.EqualTo(1));
            Assert.That(report.Applied, Has.Count.EqualTo(1));
            Assert.That(report.DeletedBytes, Is.EqualTo(7));
            Assert.That(remaining, Has.Length.EqualTo(2));
            Assert.That(Directory.Exists(doomedDir), Is.True);
        }
    }

    [Test]
    public void Отмена_между_удалением_пустых_подкаталогов_останавливается_до_родителя()
    {
        var doomedDir = Directory.CreateDirectory(Path.Combine(_rightDir, "extra")).FullName;
        var sub1 = Directory.CreateDirectory(Path.Combine(doomedDir, "sub1")).FullName;
        var sub2 = Directory.CreateDirectory(Path.Combine(doomedDir, "sub2")).FullName;

        var comparer = new DirectoryComparer(new(string.Empty), NullLogger<DirectoryComparer>.Instance);
        var comparison = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);
        comparison.ApplyMode(SyncMode.LeftToRight, mirror: true);

        var engine = new SyncEngine(
            NullLogger<SyncEngine>.Instance,
            showDeleteUi: false,
            recycleOverwritten: false,
            allowPermanentNetworkDelete: true,
            supportsRecycleBin: static _ => false);

        var report = new SyncReport();
        using var cts = new CancellationTokenSource();
        var progress = new InlineProgress(_ => cts.Cancel());

        Assert.Throws<OperationCanceledException>(() => engine.Execute(comparison, report, cts.Token, progress));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Errors, Is.Empty);
            Assert.That(report.DeletedCount, Is.EqualTo(1));
            Assert.That(report.Applied, Has.Count.EqualTo(1));
            Assert.That(Directory.Exists(doomedDir), Is.True);
            Assert.That(Directory.Exists(sub1) != Directory.Exists(sub2), Is.True);
        }
    }

    private void TryCreateJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(start);
        process?.WaitForExit();

        if (process is not { ExitCode: 0 })
        {
            Assert.Ignore("не удалось создать junction");
        }

        _links.Add(link);
    }
}
