using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Export;

namespace SpaceSnoop.Tests;

[TestFixture]
public class SyncPlanExportTests
{
    private static readonly ComparisonExportOptions Options = new(SyncMode.LeftToRight, SyncWinner.Newest, true, string.Empty);

    [Test]
    public void Build_CountsCopiedAndDeletedBytesBySourceSide()
    {
        var root = new DirectoryComparison("root", string.Empty);
        root.Files.Add(new("new.txt", "new.txt") { Status = ComparisonStatus.LeftOnly, Action = SyncAction.CopyToRight, LeftSize = 100 });
        root.Files.Add(new("old.txt", "old.txt") { Status = ComparisonStatus.RightOnly, Action = SyncAction.DeleteRight, RightSize = 30 });
        root.Files.Add(new("same.txt", "same.txt") { Status = ComparisonStatus.Identical, Action = SyncAction.Skip, LeftSize = 999, RightSize = 999 });

        var model = SyncPlanExport.Build(new("L", "R", root), Options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.CopyBytes, Is.EqualTo(100));
            Assert.That(model.DeleteBytes, Is.EqualTo(30));
            Assert.That(model.Actions.NewCopies, Is.EqualTo(1));
            Assert.That(model.Actions.Deletes, Is.EqualTo(1));
            Assert.That(model.Largest.Select(x => x.Path), Is.EqualTo(new[] { "new.txt", "old.txt" }));
        }
    }

    [Test]
    public void Build_IncludesOneSidedDirectoriesAndSkipsIdenticalOnes()
    {
        var root = new DirectoryComparison("root", string.Empty);
        root.SubDirectories.Add(new("only", "only") { Status = ComparisonStatus.LeftOnly, Action = SyncAction.CopyToRight });
        root.SubDirectories.Add(new("both", "both") { Status = ComparisonStatus.Identical, Action = SyncAction.None });

        var model = SyncPlanExport.Build(new("L", "R", root), Options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.Largest, Has.Count.EqualTo(1));
            Assert.That(model.Largest[0].Kind, Is.EqualTo(ComparisonEntryKind.Directory));
            Assert.That(model.Actions.DirCopies, Is.EqualTo(1));
        }
    }

    [Test]
    public void Build_KeepsLargestPathsAndReportsOmitted()
    {
        var root = new DirectoryComparison("root", string.Empty);

        for (var i = 0; i < 5; i++)
        {
            root.Files.Add(new($"f{i}.bin", $"f{i}.bin")
            {
                Status = ComparisonStatus.LeftOnly,
                Action = SyncAction.CopyToRight,
                LeftSize = (i + 1) * 10,
            });
        }

        var model = SyncPlanExport.Build(new("L", "R", root), Options, entryLimit: 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.Largest.Select(x => x.Path), Is.EqualTo(new[] { "f4.bin", "f3.bin" }));
            Assert.That(model.OmittedEntries, Is.EqualTo(3));
            Assert.That(model.Actions.NewCopies, Is.EqualTo(5));
        }
    }
}
