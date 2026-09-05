using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncRowsProjectorTests
{
    [Test]
    public void Плоский_вид_даёт_файлы_путями_а_дерево_каталоги_и_вложенные_строки()
    {
        var root = BuildRoot();

        var flat = Project(new() { Result = Comparison(root), FlatView = true });
        var tree = Project(new() { Result = Comparison(root) });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(flat, Is.EqualTo(new[] { "docs/report.txt", "src/main.cs" }));
            Assert.That(tree, Is.EqualTo(new[] { "docs", "report.txt", "src", "main.cs" }));
        }
    }

    [TestCase(false, false, ExpectedResult = "docs/report.txt|src/main.cs")]
    [TestCase(true, false, ExpectedResult = "docs/report.txt|docs/same.txt|readme.md|src/main.cs")]
    [TestCase(false, true, ExpectedResult = "src/main.cs")]
    [TestCase(true, true, ExpectedResult = "docs/same.txt|readme.md|src/main.cs")]
    public string Фильтры_показа_решают_состав_плоского_списка(bool showIdentical, bool hideApplied)
    {
        var root = BuildRoot();
        var applied = root.SubDirectories[0].Files[0];

        return Joined(new()
        {
            Result = Comparison(root),
            FlatView = true,
            ShowIdentical = showIdentical,
            HideApplied = hideApplied,
            Outcomes = new() { [applied] = SyncOutcome.Applied },
        });
    }

    [TestCase(false, ExpectedResult = "src|main.cs")]
    [TestCase(true, ExpectedResult = "src/main.cs")]
    public string Поиск_оставляет_совпавшие_файлы_и_ветку_до_них(bool flat)
    {
        var root = BuildRoot();

        return Joined(new()
        {
            Result = Comparison(root),
            FlatView = flat,
            SearchText = " main ",
        });
    }

    [Test]
    public void Свёрнутый_каталог_остаётся_строкой_но_прячет_содержимое()
    {
        var root = BuildRoot();
        var docs = root.SubDirectories[0];

        var rows = SyncRowsProjector.Build(
            new() { Result = Comparison(root), Collapsed = [docs] },
            new RowHost());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows.Select(Label), Is.EqualTo(new[] { "docs", "src", "main.cs" }));
            Assert.That(rows[0].IsExpanded, Is.False);
        }
    }

    [TestCase(true, ExpectedResult = "docs/report.txt|src/main.cs|Служебные файлы (1)|bin (1)|bin/app.dll")]
    [TestCase(false, ExpectedResult = "docs/report.txt|src/main.cs|Служебные файлы (1)")]
    public string Служебные_каталоги_уходят_в_группу_с_заголовком(bool groupExpanded)
    {
        var root = BuildRoot();
        root.SubDirectories.Add(Dir("bin", ComparisonStatus.Modified, File("app.dll", "bin/app.dll", ComparisonStatus.LeftOnly)));

        return Joined(new()
        {
            Result = Comparison(root),
            FlatView = true,
            GroupFolders = "bin,obj",
            GitGroupExpanded = groupExpanded,
        });
    }

    private static DirectoryComparison BuildRoot()
    {
        var root = Dir("", ComparisonStatus.Modified, File("readme.md", "readme.md", ComparisonStatus.Identical));

        root.SubDirectories.Add(Dir("docs",
            ComparisonStatus.Modified,
            File("report.txt", "docs/report.txt", ComparisonStatus.Modified),
            File("same.txt", "docs/same.txt", ComparisonStatus.Identical)));

        root.SubDirectories.Add(Dir("src", ComparisonStatus.Modified, File("main.cs", "src/main.cs", ComparisonStatus.LeftOnly)));

        return root;
    }

    private static ComparisonResult Comparison(DirectoryComparison root)
    {
        return new(@"C:\left", @"C:\right", root);
    }

    private static DirectoryComparison Dir(string name, ComparisonStatus status, params FileComparison[] files)
    {
        var dir = new DirectoryComparison(name, name) { Status = status };
        dir.Files.AddRange(files);
        return dir;
    }

    private static FileComparison File(string name, string relativePath, ComparisonStatus status)
    {
        return new(name, relativePath) { Status = status };
    }

    private static string[] Project(SyncRowsRequest request)
    {
        return SyncRowsProjector.Build(request, new RowHost()).Select(Label).ToArray();
    }

    private static string Joined(SyncRowsRequest request)
    {
        return string.Join('|', Project(request));
    }

    private static string Label(SyncNodeViewModel row)
    {
        return row.IsGroupHeader ? row.GroupHeaderText : row.DisplayName;
    }

    private sealed class RowHost : ISyncRowHost
    {
        public bool BlankAbsent => false;

        public bool ChatEnabled => false;

        public void ToggleExpand(DirectoryComparison dir)
        {
        }

        public void ToggleGroup(string? key)
        {
        }

        public void ExpandSubtree(DirectoryComparison dir)
        {
        }

        public void CollapseSubtree(DirectoryComparison dir)
        {
        }

        public void ApplyToSubtree(DirectoryComparison dir, SyncAction action)
        {
        }

        public void NotifyActionsChanged()
        {
        }

        public Task CompareContentAsync(FileComparison file)
        {
            return Task.CompletedTask;
        }

        public void AskAgentAbout(SyncNodeViewModel node)
        {
        }
    }
}
