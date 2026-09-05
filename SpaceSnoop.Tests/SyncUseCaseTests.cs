using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.UseCases;

namespace SpaceSnoop.Tests;

[TestFixture]
public class SyncUseCaseTests
{
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopTest_{Guid.NewGuid():N}");
        _leftDir = Path.Combine(_tempDir, "left");
        _rightDir = Path.Combine(_tempDir, "right");
        Directory.CreateDirectory(_leftDir);
        Directory.CreateDirectory(_rightDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private string _tempDir = null!;
    private string _leftDir = null!;
    private string _rightDir = null!;

    [Test]
    public void Сравнение_обрезает_пробелы_вокруг_путей()
    {
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "данные");

        var result = Compare().Execute(new($"  {_leftDir} ", $" {_rightDir}  ", string.Empty, SyncMode.LeftToRight, SyncWinner.Newest, false), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.LeftPath, Is.EqualTo(_leftDir));
            Assert.That(result.RightPath, Is.EqualTo(_rightDir));
            Assert.That(result.Root.Files, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void Сравнение_строит_фильтр_исключений_из_строки_запроса()
    {
        File.WriteAllText(Path.Combine(_leftDir, "keep.txt"), "данные");
        File.WriteAllText(Path.Combine(_leftDir, "skip.tmp"), "данные");

        var result = Compare().Execute(new(_leftDir, _rightDir, "*.tmp", SyncMode.LeftToRight, SyncWinner.Newest, false), CancellationToken.None);

        Assert.That(result.Root.Files.Select(file => file.Name), Is.EqualTo(new[] { "keep.txt" }));
    }

    [TestCase(SyncMode.LeftToRight, false, SyncWinner.Newest, ExpectedResult = SyncAction.CopyToRight)]
    [TestCase(SyncMode.RightToLeft, false, SyncWinner.Newest, ExpectedResult = SyncAction.Skip)]
    [TestCase(SyncMode.RightToLeft, true, SyncWinner.Newest, ExpectedResult = SyncAction.DeleteLeft)]
    [TestCase(SyncMode.Bidirectional, false, SyncWinner.Right, ExpectedResult = SyncAction.CopyToRight)]
    [TestCase(SyncMode.Bidirectional, true, SyncWinner.Right, ExpectedResult = SyncAction.DeleteLeft)]
    public SyncAction Сравнение_применяет_режим_зеркало_и_победителя(SyncMode mode, bool mirror, SyncWinner winner)
    {
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "данные");

        var result = Compare().Execute(new(_leftDir, _rightDir, string.Empty, mode, winner, mirror), CancellationToken.None);

        return result.Root.Files[0].Action;
    }

    [TestCase(SyncConflictPolicy.SkipUnresolved, ExpectedResult = false)]
    [TestCase(SyncConflictPolicy.None, ExpectedResult = true)]
    public bool Синхронизация_разрешает_конфликты_только_по_политике(SyncConflictPolicy policy)
    {
        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("a.txt", "a.txt") { Status = ComparisonStatus.Conflict, Action = SyncAction.None });

        var comparison = new ComparisonResult(_leftDir, _rightDir, root);
        Sync().Execute(new(comparison, policy), CancellationToken.None);

        return comparison.HasUnresolvedConflicts();
    }

    [TestCase(true, ExpectedResult = true)]
    [TestCase(false, ExpectedResult = false)]
    public bool Синхронизация_помечает_отчёт_проверенным_только_по_запросу(bool verify)
    {
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "данные");

        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("a.txt", "a.txt") { Status = ComparisonStatus.LeftOnly, Action = SyncAction.CopyToRight });

        var comparison = new ComparisonResult(_leftDir, _rightDir, root);
        var report = Sync().Execute(new(comparison, VerifyAfterSync: verify), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.SuccessCount, Is.EqualTo(1));
            Assert.That(report.Mismatches, Is.Empty);
        }

        return report.Verified;
    }

    private static CompareDirectoriesUseCase Compare()
    {
        return new(NullLogger<DirectoryComparer>.Instance);
    }

    private static ExecuteSyncUseCase Sync()
    {
        return new(NullLogger<SyncEngine>.Instance);
    }
}
