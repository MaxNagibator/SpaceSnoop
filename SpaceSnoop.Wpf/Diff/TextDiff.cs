namespace SpaceSnoop.Wpf.Diff;

public static class TextDiff
{
    private const long MaxCells = 4_000_000;

    public static IReadOnlyList<DiffLine> Compute(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var start = 0;
        var leftEnd = left.Count;
        var rightEnd = right.Count;

        while (start < leftEnd && start < rightEnd && left[start] == right[start])
        {
            start++;
        }

        while (leftEnd > start && rightEnd > start && left[leftEnd - 1] == right[rightEnd - 1])
        {
            leftEnd--;
            rightEnd--;
        }

        var result = new List<DiffLine>(left.Count + right.Count);

        for (var i = 0; i < start; i++)
        {
            result.Add(new(DiffLineKind.Context, left[i], i + 1, i + 1));
        }

        DiffMiddle(left, right, start, leftEnd, rightEnd, result);

        for (var i = leftEnd; i < left.Count; i++)
        {
            result.Add(new(DiffLineKind.Context, left[i], i + 1, rightEnd + (i - leftEnd) + 1));
        }

        return result;
    }

    public static IReadOnlyList<DiffRow> ToSideBySide(IReadOnlyList<DiffLine> lines)
    {
        var rows = new List<DiffRow>(lines.Count);
        var removed = new List<DiffLine>();
        var added = new List<DiffLine>();

        void Flush()
        {
            var max = Math.Max(removed.Count, added.Count);

            for (var i = 0; i < max; i++)
            {
                var l = i < removed.Count ? removed[i] : null;
                var r = i < added.Count ? added[i] : null;

                rows.Add(new(l is null ? DiffLineKind.None : DiffLineKind.Removed,
                    l?.LeftNumberText ?? string.Empty,
                    l?.Text ?? string.Empty,
                    r is null ? DiffLineKind.None : DiffLineKind.Added,
                    r?.RightNumberText ?? string.Empty,
                    r?.Text ?? string.Empty));
            }

            removed.Clear();
            added.Clear();
        }

        foreach (var line in lines)
        {
            switch (line.Kind)
            {
                case DiffLineKind.Removed:
                    removed.Add(line);
                    break;

                case DiffLineKind.Added:
                    added.Add(line);
                    break;

                default:
                    Flush();
                    rows.Add(new(DiffLineKind.Context, line.LeftNumberText, line.Text, DiffLineKind.Context, line.RightNumberText, line.Text));
                    break;
            }
        }

        Flush();
        return rows;
    }

    private static void DiffMiddle(IReadOnlyList<string> left, IReadOnlyList<string> right, int leftStart, int leftEnd, int rightEnd, List<DiffLine> output)
    {
        var n = leftEnd - leftStart;
        var m = rightEnd - leftStart;

        // TODO: LCS-таблица O(n*m); при огромной середине деградируем в блочную замену
        //           (память O(строк), не O(n*m)). Апгрейд — Myers/DiffPlex, если станет мало.
        if (n == 0 || m == 0 || (long)n * m > MaxCells)
        {
            for (var i = leftStart; i < leftEnd; i++)
            {
                output.Add(new(DiffLineKind.Removed, left[i], i + 1, 0));
            }

            for (var j = leftStart; j < rightEnd; j++)
            {
                output.Add(new(DiffLineKind.Added, right[j], 0, j + 1));
            }

            return;
        }

        var dp = new int[n + 1, m + 1];

        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                dp[i, j] = left[leftStart + i] == right[leftStart + j]
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        int a = 0, b = 0;

        while (a < n && b < m)
        {
            if (left[leftStart + a] == right[leftStart + b])
            {
                output.Add(new(DiffLineKind.Context, left[leftStart + a], leftStart + a + 1, leftStart + b + 1));
                a++;
                b++;
            }
            else if (dp[a + 1, b] >= dp[a, b + 1])
            {
                output.Add(new(DiffLineKind.Removed, left[leftStart + a], leftStart + a + 1, 0));
                a++;
            }
            else
            {
                output.Add(new(DiffLineKind.Added, right[leftStart + b], 0, leftStart + b + 1));
                b++;
            }
        }

        while (a < n)
        {
            output.Add(new(DiffLineKind.Removed, left[leftStart + a], leftStart + a + 1, 0));
            a++;
        }

        while (b < m)
        {
            output.Add(new(DiffLineKind.Added, right[leftStart + b], 0, leftStart + b + 1));
            b++;
        }
    }
}
