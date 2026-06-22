using SpaceSnoop.Wpf.Diff;
using System.Globalization;

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

    [Test]
    public void Myers_сохраняет_внутренний_общий_фрагмент()
    {
        var diff = TextDiff.Compute(["1", "2", "3", "4"], ["1", "9", "3", "8"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diff.Any(static l => l.Kind == DiffLineKind.Context && l.Text == "3"), "общая строка «3» в середине должна остаться контекстом");
            Assert.That(diff.Count(static l => l.Kind == DiffLineKind.Removed), Is.EqualTo(2));
            Assert.That(diff.Count(static l => l.Kind == DiffLineKind.Added), Is.EqualTo(2));
        }
    }

    [Test]
    public void Myers_разреженные_правки_в_большом_файле_не_деградируют_в_блочную_замену()
    {
        var left = Enumerable.Range(0, 3000).Select(static i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        var right = (string[])left.Clone();
        var changes = 0;

        for (var i = 100; i <= 2900; i += 50)
        {
            right[i] = "X" + i.ToString(CultureInfo.InvariantCulture);
            changes++;
        }

        var diff = TextDiff.Compute(left, right);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diff.Count(static l => l.Kind == DiffLineKind.Removed), Is.EqualTo(changes), "правок мало – блочная замена дала бы тысячи удалений");
            Assert.That(diff.Count(static l => l.Kind == DiffLineKind.Added), Is.EqualTo(changes));
        }
    }

    private static IReadOnlyList<DiffLine> SingleChangeDiff()
    {
        var left = Enumerable.Range(0, 30).Select(static i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        var right = (string[])left.Clone();
        right[0] = "X";
        return TextDiff.Compute(left, right);
    }

    [Test]
    public void Collapse_сворачивает_длинный_неизменный_блок_сохраняя_контекст()
    {
        var lines = SingleChangeDiff();

        var segments = TextDiff.Collapse(lines, 3, new HashSet<int>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(segments.Any(static s => s.IsGap), "длинный неизменный хвост должен схлопнуться");
            Assert.That(segments.Where(static s => !s.IsGap).Where(s => lines[s.Start].Kind == DiffLineKind.Context).Sum(static s => s.Count), Is.EqualTo(3), "вокруг изменения остаётся ровно context строк");
            Assert.That(segments.Sum(static s => s.Count), Is.EqualTo(lines.Count), "сегменты покрывают все строки без потерь");
        }
    }

    [Test]
    public void Collapse_не_трогает_короткий_неизменный_блок()
    {
        var lines = TextDiff.Compute(["A", "b", "c", "D"], ["A2", "b", "c", "D2"]);

        var segments = TextDiff.Collapse(lines, 3, new HashSet<int>());

        Assert.That(segments.Any(static s => s.IsGap), Is.False);
    }

    [Test]
    public void Collapse_разворачивает_сегмент_по_ключу()
    {
        var lines = SingleChangeDiff();
        var gap = TextDiff.Collapse(lines, 3, new HashSet<int>()).First(static s => s.IsGap);

        var expanded = TextDiff.Collapse(lines, 3, new HashSet<int> { gap.Start });

        Assert.That(expanded.Any(static s => s.IsGap), Is.False);
    }
}
