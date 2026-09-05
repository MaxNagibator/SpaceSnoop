using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Export;
using System.Text.Json;

namespace SpaceSnoop.Tests;

[TestFixture]
public class ComparisonExportTests
{
    private static readonly ComparisonExportOptions Options = new(SyncMode.LeftToRight, SyncWinner.Newest, false, "bin,obj");

    [Test]
    public void Build_SkipsIdenticalFilesAndKeepsDifferences()
    {
        var root = new DirectoryComparison("root", string.Empty);
        root.Files.Add(new("same.txt", "same.txt") { Status = ComparisonStatus.Identical, Action = SyncAction.Skip });
        root.Files.Add(new("new.txt", "new.txt") { Status = ComparisonStatus.LeftOnly, Action = SyncAction.CopyToRight, LeftSize = 10 });

        var model = ComparisonExport.Build(new("L", "R", root), Options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.Entries.Select(x => x.Path), Is.EqualTo(new[] { "new.txt" }));
            Assert.That(model.Entries[0].Kind, Is.EqualTo(ComparisonEntryKind.File));
            Assert.That(model.Totals.Files["Identical"], Is.EqualTo(1));
            Assert.That(model.Totals.Planned.NewCopies, Is.EqualTo(1));
            Assert.That(model.OmittedEntries, Is.Zero);
        }
    }

    [Test]
    public void Build_DescribesModifiedFileBySizeAndTimeDelta()
    {
        var left = new DateTime(2026, 7, 21, 12, 0, 30, DateTimeKind.Local);
        var root = new DirectoryComparison("root", string.Empty);

        root.Files.Add(new("a.txt", "a.txt")
        {
            Status = ComparisonStatus.Modified,
            Action = SyncAction.CopyToRight,
            LeftSize = 100,
            RightSize = 100,
            LeftModified = left,
            RightModified = left.AddSeconds(-10),
        });

        var entry = ComparisonExport.Build(new("L", "R", root), Options).Entries[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.SizeDiffers, Is.False);
            Assert.That(entry.TimeDeltaSeconds, Is.EqualTo(10));
        }
    }

    [Test]
    public void Build_IncludesOneSidedDirectoriesAsEntries()
    {
        var root = new DirectoryComparison("root", string.Empty);
        var sub = new DirectoryComparison("only", "only") { Status = ComparisonStatus.RightOnly, Action = SyncAction.CopyToLeft };
        root.SubDirectories.Add(sub);

        var model = ComparisonExport.Build(new("L", "R", root), Options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.Entries, Has.Count.EqualTo(1));
            Assert.That(model.Entries[0].Kind, Is.EqualTo(ComparisonEntryKind.Directory));
            Assert.That(model.Entries[0].Action, Is.EqualTo(SyncAction.CopyToLeft));
        }
    }

    [Test]
    public void Build_KeepsLargestEntriesWhenLimitExceeded()
    {
        var root = new DirectoryComparison("root", string.Empty);

        for (var i = 0; i < 5; i++)
        {
            root.Files.Add(new($"f{i}.bin", $"f{i}.bin")
            {
                Status = ComparisonStatus.LeftOnly,
                Action = SyncAction.CopyToRight,
                LeftSize = i,
            });
        }

        var model = ComparisonExport.Build(new("L", "R", root), Options, entryLimit: 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.Entries.Select(x => x.Path), Is.EqualTo(new[] { "f3.bin", "f4.bin" }));
            Assert.That(model.OmittedEntries, Is.EqualTo(3));
        }
    }

    [Test]
    public void Build_SummarizesTopLevelDirectoriesByDifferingBytes()
    {
        var root = new DirectoryComparison("root", string.Empty);
        root.Files.Add(new("root.txt", "root.txt") { Status = ComparisonStatus.LeftOnly, LeftSize = 1 });

        var small = new DirectoryComparison("small", "small");
        small.Files.Add(new("s.txt", @"small\s.txt") { Status = ComparisonStatus.Modified, LeftSize = 5, RightSize = 7 });

        var big = new DirectoryComparison("big", "big");
        var nested = new DirectoryComparison("nested", @"big\nested");
        nested.Files.Add(new("b.txt", @"big\nested\b.txt") { Status = ComparisonStatus.RightOnly, RightSize = 100 });
        big.SubDirectories.Add(nested);

        root.SubDirectories.Add(small);
        root.SubDirectories.Add(big);

        var directories = ComparisonExport.Build(new("L", "R", root), Options).Directories;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(directories.Select(x => x.Path), Is.EqualTo(new[] { "big", "small", "." }));
            Assert.That(directories[0].DifferingBytes, Is.EqualTo(100));
            Assert.That(directories[0].RightOnly, Is.EqualTo(1));
            Assert.That(directories[1].Modified, Is.EqualTo(1));
        }
    }

    [Test]
    public void ToJson_WritesCamelCaseWithReadableEnumsAndPaths()
    {
        var root = new DirectoryComparison("root", string.Empty);
        root.Files.Add(new("отчёт.txt", "отчёт.txt") { Status = ComparisonStatus.LeftOnly, Action = SyncAction.CopyToRight });

        var json = ComparisonExport.ToJson(ComparisonExport.Build(new(@"C:\left", @"C:\right", root), Options));
        var document = JsonDocument.Parse(json);
        var entry = document.RootElement.GetProperty("entries")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(document.RootElement.GetProperty("leftPath").GetString(), Is.EqualTo(@"C:\left"));
            Assert.That(document.RootElement.GetProperty("options").GetProperty("mode").GetString(), Is.EqualTo("LeftToRight"));
            Assert.That(entry.GetProperty("path").GetString(), Is.EqualTo("отчёт.txt"));
            Assert.That(entry.GetProperty("status").GetString(), Is.EqualTo("LeftOnly"));
            Assert.That(json, Does.Contain("отчёт.txt"));
        }
    }
}
