using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.ViewModels.Dialogs;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class FileDiffHiddenDifferenceTests
{
    private static readonly DateTime Base = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);

    private static FileComparison Modified(long leftSize, long rightSize, int secondsApart)
    {
        return new("a.txt", "a.txt")
        {
            Status = ComparisonStatus.Modified,
            LeftSize = leftSize,
            RightSize = rightSize,
            LeftModified = Base,
            RightModified = Base.AddSeconds(secondsApart),
        };
    }

    [Test]
    public void Различие_по_размеру_намекает_на_переводы_строк()
    {
        var text = FileDiffDialogViewModel.DescribeHiddenDifference(Modified(100, 103, 0));

        Assert.That(text, Does.Contain("Размер отличается").And.Contain("CRLF"));
    }

    [Test]
    public void Различие_по_времени_объясняет_безвредность()
    {
        var text = FileDiffDialogViewModel.DescribeHiddenDifference(Modified(100, 100, 30));

        Assert.That(text, Does.Contain("время").And.Contain("на содержимое не влияет"));
    }

    [Test]
    public void Различие_по_обоим_показывает_размер_и_время()
    {
        var text = FileDiffDialogViewModel.DescribeHiddenDifference(Modified(100, 200, 3600));

        Assert.That(text, Does.Contain("Размер отличается").And.Contain("время"));
    }

    [Test]
    public void В_пределах_допуска_относит_к_метаданным()
    {
        var text = FileDiffDialogViewModel.DescribeHiddenDifference(Modified(100, 100, 1));

        Assert.That(text, Does.Contain("метаданных"));
    }

    [TestCase(ComparisonStatus.LeftOnly, "только слева")]
    [TestCase(ComparisonStatus.RightOnly, "только справа")]
    public void Односторонний_файл_объясняет_отсутствующую_сторону(ComparisonStatus status, string expected)
    {
        var file = new FileComparison("a.txt", "a.txt")
        {
            Status = status,
            LeftSize = status == ComparisonStatus.LeftOnly ? 2048 : null,
            RightSize = status == ComparisonStatus.RightOnly ? 2048 : null,
        };

        var text = FileDiffDialogViewModel.DescribeOneSided(file);

        Assert.That(text, Does.Contain(expected).And.Contain("2"));
    }

    [TestCase(ComparisonStatus.Identical)]
    [TestCase(ComparisonStatus.Modified)]
    public void Двусторонний_файл_не_получает_пояснения_об_отсутствии(ComparisonStatus status)
    {
        var file = Modified(100, 100, 0);
        file.Status = status;

        Assert.That(FileDiffDialogViewModel.DescribeOneSided(file), Is.Empty);
    }
}
