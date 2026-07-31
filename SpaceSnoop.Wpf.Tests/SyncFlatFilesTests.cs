using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncFlatFilesTests
{
    private static readonly IReadOnlyDictionary<object, SyncOutcome> NoOutcomes = new Dictionary<object, SyncOutcome>();

    [Test]
    public void Свёртка_идёт_в_глубину_подкаталоги_раньше_собственных_файлов()
    {
        var rootFile = File("root.txt", ComparisonStatus.LeftOnly);
        var nested = File("a/inner.txt", ComparisonStatus.Modified);
        var root = Dir("", rootFile);
        root.SubDirectories.Add(Dir("a", nested));

        var files = SyncViewModel.CollectVisibleFiles(root, false, false, NoOutcomes).ToList();

        Assert.That(files, Is.EqualTo([nested, rootFile]));
    }

    [Test]
    public void Одинаковые_файлы_скрыты_пока_не_включён_показ()
    {
        var same = File("same.txt", ComparisonStatus.Identical);
        var diff = File("diff.txt", ComparisonStatus.Modified);
        var root = Dir("", same, diff);

        var hidden = SyncViewModel.CollectVisibleFiles(root, false, false, NoOutcomes).ToList();
        var shown = SyncViewModel.CollectVisibleFiles(root, true, false, NoOutcomes).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hidden, Is.EqualTo([diff]));
            Assert.That(shown, Is.EqualTo([same, diff]));
        }
    }

    [Test]
    public void Применённые_файлы_прячутся_при_скрыть_применённые()
    {
        var applied = File("done.txt", ComparisonStatus.LeftOnly);
        var failed = File("fail.txt", ComparisonStatus.LeftOnly);
        var root = Dir("", applied, failed);
        var outcomes = new Dictionary<object, SyncOutcome>
        {
            [applied] = SyncOutcome.Applied,
            [failed] = SyncOutcome.Failed,
        };

        var files = SyncViewModel.CollectVisibleFiles(root, false, true, outcomes).ToList();

        Assert.That(files, Is.EqualTo([failed]));
    }

    [Test]
    public void Сортировка_по_размеру_берёт_большую_из_сторон()
    {
        var small = new FileComparison("s.txt", "s.txt") { LeftSize = 10, RightSize = 5 };
        var big = new FileComparison("b.txt", "b.txt") { LeftSize = 100, RightSize = 0 };
        var mid = new FileComparison("m.txt", "m.txt") { LeftSize = null, RightSize = 50 };

        var asc = SyncViewModel.SortFiles([small, big, mid], SyncSortField.Size, false).ToList();
        var desc = SyncViewModel.SortFiles([small, big, mid], SyncSortField.Size, true).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(asc, Is.EqualTo([small, mid, big]));
            Assert.That(desc, Is.EqualTo([big, mid, small]));
        }
    }

    [Test]
    public void Сортировка_по_пути_регистронезависима()
    {
        var a = new FileComparison("a.txt", "alpha/Zebra.txt");
        var b = new FileComparison("b.txt", "alpha/apple.txt");

        var sorted = SyncViewModel.SortFiles([a, b], SyncSortField.Path, false).ToList();

        Assert.That(sorted, Is.EqualTo([b, a]));
    }

    [Test]
    public void Сортировка_по_статусу_группирует_и_упорядочивает_по_пути()
    {
        var modifiedZ = new FileComparison("z.txt", "z.txt") { Status = ComparisonStatus.Modified };
        var modifiedA = new FileComparison("a.txt", "a.txt") { Status = ComparisonStatus.Modified };
        var leftOnly = new FileComparison("l.txt", "l.txt") { Status = ComparisonStatus.LeftOnly };

        var sorted = SyncViewModel.SortFiles([modifiedZ, leftOnly, modifiedA], SyncSortField.Status, false).ToList();

        Assert.That(sorted, Is.EqualTo([leftOnly, modifiedA, modifiedZ]));
    }

    [Test]
    public void Сортировка_по_дате_берёт_позднюю_из_сторон()
    {
        var old = new FileComparison("o.txt", "o.txt") { LeftModified = new(2026, 1, 1), RightModified = new(2025, 1, 1) };
        var fresh = new FileComparison("f.txt", "f.txt") { LeftModified = null, RightModified = new(2026, 6, 1) };
        var never = new FileComparison("n.txt", "n.txt");

        var asc = SyncViewModel.SortFiles([old, fresh, never], SyncSortField.Modified, false).ToList();
        var desc = SyncViewModel.SortFiles([old, fresh, never], SyncSortField.Modified, true).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(asc, Is.EqualTo([never, old, fresh]));
            Assert.That(desc, Is.EqualTo([fresh, old, never]));
        }
    }

    [Test]
    public void Фильтр_дерева_помечает_ветки_с_совпадением_в_глубине()
    {
        var deep = Dir("deep", File("keep/deep/target.txt", ComparisonStatus.Modified));
        var keep = Dir("keep");
        keep.SubDirectories.Add(deep);
        var other = Dir("other", File("other/plain.txt", ComparisonStatus.Modified));
        var root = Dir("");
        root.SubDirectories.Add(keep);
        root.SubDirectories.Add(other);

        var hits = new HashSet<object>();
        SyncViewModel.CollectSearchHits(root, "target", hits);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits, Does.Contain(keep));
            Assert.That(hits, Does.Contain(deep));
            Assert.That(hits, Does.Not.Contain(other));
        }
    }

    [Test]
    public void Фильтр_дерева_принимает_совпадение_по_имени_каталога()
    {
        var assets = Dir("assets", File("assets/plain.txt", ComparisonStatus.Modified));
        var root = Dir("");
        root.SubDirectories.Add(assets);

        var hits = new HashSet<object>();
        SyncViewModel.CollectSearchHits(root, "ASSETS", hits);

        Assert.That(hits, Does.Contain(assets));
    }

    [Test]
    public void Пустые_односторонние_каталоги_берутся_верхним_уровнем_без_файловых()
    {
        var kept = OneSided("kept", ComparisonStatus.LeftOnly, File("kept/x.txt", ComparisonStatus.LeftOnly));
        var leaf = OneSided("leaf", ComparisonStatus.LeftOnly);
        var branch = OneSided("branch", ComparisonStatus.LeftOnly);
        branch.SubDirectories.Add(leaf);
        var solo = OneSided("solo", ComparisonStatus.RightOnly);

        var root = Dir("");
        root.SubDirectories.Add(kept);
        root.SubDirectories.Add(branch);
        root.SubDirectories.Add(solo);

        var dirs = SyncViewModel.CollectEmptyDirs(root, false, NoOutcomes).ToList();

        Assert.That(dirs, Is.EqualTo([branch, solo]));
    }

    private static DirectoryComparison OneSided(string name, ComparisonStatus status, params FileComparison[] files)
    {
        var dir = Dir(name, files);
        dir.Status = status;
        return dir;
    }

    private static FileComparison File(string relativePath, ComparisonStatus status)
    {
        var name = relativePath.Split('/')[^1];
        return new(name, relativePath) { Status = status };
    }

    private static DirectoryComparison Dir(string name, params FileComparison[] files)
    {
        var dir = new DirectoryComparison(name, name);
        dir.Files.AddRange(files);
        return dir;
    }
}
