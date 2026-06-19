using SpaceSnoop.Wpf.Diff;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class TextDiffTests
{
    [Test]
    public void Идентичные_файлы_дают_только_контекст()
    {
        string[] lines = ["a", "b", "c"];

        var diff = TextDiff.Compute(lines, lines);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diff.All(static l => l.Kind == DiffLineKind.Context));
            Assert.That(diff, Has.Count.EqualTo(3));
        }
    }

    private static IEnumerable<TestCaseData> ChangeCases()
    {
        yield return new TestCaseData(new[] { "a", "c" },
            new[] { "a", "b", "c" },
            0, 1).SetName("Вставка_строки_даёт_одно_добавление");

        yield return new TestCaseData(new[] { "a", "b", "c" },
            new[] { "a", "c" },
            1, 0).SetName("Удаление_строки_даёт_одно_удаление");

        yield return new TestCaseData(new[] { "a", "b", "c" },
            new[] { "a", "B", "c" },
            1, 1).SetName("Модификация_строки_даёт_удаление_и_добавление");

        yield return new TestCaseData(Array.Empty<string>(),
            new[] { "a", "b" },
            0, 2).SetName("Пустой_левый_файл_всё_добавлено");

        yield return new TestCaseData(new[] { "x", "y" },
            new[] { "p", "q" },
            2, 2).SetName("Полностью_разные_файлы");
    }

    [TestCaseSource(nameof(ChangeCases))]
    public void Diff_считает_добавления_и_удаления(string[] left, string[] right, int expectedRemoved, int expectedAdded)
    {
        var diff = TextDiff.Compute(left, right);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diff.Count(static l => l.Kind == DiffLineKind.Removed), Is.EqualTo(expectedRemoved));
            Assert.That(diff.Count(static l => l.Kind == DiffLineKind.Added), Is.EqualTo(expectedAdded));
        }
    }

    [Test]
    public void Side_by_side_выравнивает_изменённую_строку_в_одну_строку()
    {
        var diff = TextDiff.Compute(["a", "b", "c"], ["a", "B", "c"]);

        var rows = TextDiff.ToSideBySide(diff);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows, Has.Count.EqualTo(3));
            Assert.That(rows[1].LeftKind, Is.EqualTo(DiffLineKind.Removed));
            Assert.That(rows[1].LeftText, Is.EqualTo("b"));
            Assert.That(rows[1].RightKind, Is.EqualTo(DiffLineKind.Added));
            Assert.That(rows[1].RightText, Is.EqualTo("B"));
        }
    }

    [Test]
    public void Side_by_side_добивает_филлером_неравные_блоки()
    {
        var diff = TextDiff.Compute(["a"], ["x", "y", "z"]);

        var rows = TextDiff.ToSideBySide(diff);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows, Has.Count.EqualTo(3));
            Assert.That(rows[0].LeftKind, Is.EqualTo(DiffLineKind.Removed));
            Assert.That(rows[1].LeftKind, Is.EqualTo(DiffLineKind.None));
            Assert.That(rows[2].LeftKind, Is.EqualTo(DiffLineKind.None));
            Assert.That(rows.All(static r => r.RightKind == DiffLineKind.Added));
        }
    }
}
