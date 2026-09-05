using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncDiffReasonTests
{
    private static readonly DateTime Base = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);

    [TestCase(10, 20, 0, ExpectedResult = "размер")]
    [TestCase(10, 10, 5, ExpectedResult = "время (Δ 5 с)")]
    [TestCase(10, 10, 1, ExpectedResult = "")]
    [TestCase(10, 20, 3600, ExpectedResult = "размер и время (Δ 1 ч)")]
    public string Причина_различия_объясняет_вердикт(long leftSize, long rightSize, int secondsApart)
    {
        var file = new FileComparison("a.txt", "a.txt")
        {
            Status = ComparisonStatus.Modified,
            LeftSize = leftSize,
            RightSize = rightSize,
            LeftModified = Base,
            RightModified = Base.AddSeconds(secondsApart),
        };

        return SyncNodeText.DescribeDiff(file);
    }

    [Test]
    public void Не_изменённый_файл_без_причины()
    {
        var file = new FileComparison("a.txt", "a.txt") { Status = ComparisonStatus.Identical };

        Assert.That(SyncNodeText.DescribeDiff(file), Is.Empty);
    }
}
