namespace SpaceSnoop.Wpf.Diff;

public static class TextDiff
{
    private const long MaxTraceCells = 4_000_000;

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

    public static IReadOnlyList<DiffSegment> Collapse(IReadOnlyList<DiffLine> lines, int context, IReadOnlySet<int> expanded)
    {
        var segments = new List<DiffSegment>();
        var n = lines.Count;
        var i = 0;

        while (i < n)
        {
            if (lines[i].Kind != DiffLineKind.Context)
            {
                var changeStart = i;

                while (i < n && lines[i].Kind != DiffLineKind.Context)
                {
                    i++;
                }

                segments.Add(new(false, changeStart, i - changeStart));
                continue;
            }

            var runStart = i;

            while (i < n && lines[i].Kind == DiffLineKind.Context)
            {
                i++;
            }

            AddContextRun(segments, runStart, i, n, context, expanded);
        }

        return segments;
    }

    public static (IReadOnlyList<DiffSpan> Left, IReadOnlyList<DiffSpan> Right) HighlightInline(DiffRow row)
    {
        if (row.LeftKind != DiffLineKind.Removed || row.RightKind != DiffLineKind.Added)
        {
            return (Whole(row.LeftText), Whole(row.RightText));
        }

        var left = row.LeftText;
        var right = row.RightText;
        var min = Math.Min(left.Length, right.Length);

        var prefix = 0;

        while (prefix < min && left[prefix] == right[prefix])
        {
            prefix++;
        }

        var suffix = 0;

        while (suffix < min - prefix && left[left.Length - 1 - suffix] == right[right.Length - 1 - suffix])
        {
            suffix++;
        }

        return (BuildSpans(left, prefix, suffix), BuildSpans(right, prefix, suffix));
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

    private static IReadOnlyList<DiffSpan> Whole(string text)
    {
        return [new(text, false)];
    }

    private static IReadOnlyList<DiffSpan> BuildSpans(string text, int prefix, int suffix)
    {
        var spans = new List<DiffSpan>(3);

        if (prefix > 0)
        {
            spans.Add(new(text[..prefix], false));
        }

        var midLength = text.Length - prefix - suffix;

        if (midLength > 0)
        {
            spans.Add(new(text.Substring(prefix, midLength), true));
        }

        if (suffix > 0)
        {
            spans.Add(new(text[^suffix..], false));
        }

        if (spans.Count == 0)
        {
            spans.Add(new(string.Empty, false));
        }

        return spans;
    }

    private static void AddContextRun(List<DiffSegment> segments, int start, int end, int total, int context, IReadOnlySet<int> expanded)
    {
        var length = end - start;
        var keepPrefix = start == 0 ? 0 : context;
        var keepSuffix = end == total ? 0 : context;
        var gapStart = start + keepPrefix;
        var gapCount = length - keepPrefix - keepSuffix;

        if (gapCount < 2 || expanded.Contains(gapStart))
        {
            segments.Add(new(false, start, length));
            return;
        }

        if (keepPrefix > 0)
        {
            segments.Add(new(false, start, keepPrefix));
        }

        segments.Add(new(true, gapStart, gapCount));

        if (keepSuffix > 0)
        {
            segments.Add(new(false, end - keepSuffix, keepSuffix));
        }
    }

    private static void DiffMiddle(IReadOnlyList<string> left, IReadOnlyList<string> right, int leftStart, int leftEnd, int rightEnd, List<DiffLine> output)
    {
        var n = leftEnd - leftStart;
        var m = rightEnd - leftStart;

        if (n > 0 && m > 0 && TryMyers(left, right, leftStart, n, m, output))
        {
            return;
        }

        for (var i = leftStart; i < leftEnd; i++)
        {
            output.Add(new(DiffLineKind.Removed, left[i], i + 1, 0));
        }

        for (var j = leftStart; j < rightEnd; j++)
        {
            output.Add(new(DiffLineKind.Added, right[j], 0, j + 1));
        }
    }

    // TODO: Myers O(ND); потолок теперь по числу правок, не по размеру – снимок V на правку, память ≤ ~16 МБ
    //           (MaxTraceCells). Слишком много правок в огромной середине → false → блочная замена. Апгрейд – линейный Hirschberg.
    private static bool TryMyers(IReadOnlyList<string> left, IReadOnlyList<string> right, int leftStart, int n, int m, List<DiffLine> output)
    {
        var max = n + m;
        var offset = max;
        var maxD = (int)Math.Min(max, MaxTraceCells / (2L * max + 1));
        var v = new int[2 * max + 1];
        var trace = new List<int[]>();

        var foundD = -1;

        for (var d = 0; d <= max && foundD < 0; d++)
        {
            if (d > maxD)
            {
                return false;
            }

            trace.Add((int[])v.Clone());

            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || k != d && v[offset + k - 1] < v[offset + k + 1]
                    ? v[offset + k + 1]
                    : v[offset + k - 1] + 1;

                var y = x - k;

                while (x < n && y < m && left[leftStart + x] == right[leftStart + y])
                {
                    x++;
                    y++;
                }

                v[offset + k] = x;

                if (x >= n && y >= m)
                {
                    foundD = d;
                    break;
                }
            }
        }

        var edits = new List<DiffLine>();
        int px = n, py = m;

        for (var d = foundD; d > 0; d--)
        {
            var prev = trace[d];
            var k = px - py;
            var prevK = k == -d || k != d && prev[offset + k - 1] < prev[offset + k + 1]
                ? k + 1
                : k - 1;

            var prevX = prev[offset + prevK];
            var prevY = prevX - prevK;

            while (px > prevX && py > prevY)
            {
                px--;
                py--;
                edits.Add(new(DiffLineKind.Context, left[leftStart + px], leftStart + px + 1, leftStart + py + 1));
            }

            if (px == prevX)
            {
                py--;
                edits.Add(new(DiffLineKind.Added, right[leftStart + py], 0, leftStart + py + 1));
            }
            else
            {
                px--;
                edits.Add(new(DiffLineKind.Removed, left[leftStart + px], leftStart + px + 1, 0));
            }
        }

        while (px > 0 && py > 0)
        {
            px--;
            py--;
            edits.Add(new(DiffLineKind.Context, left[leftStart + px], leftStart + px + 1, leftStart + py + 1));
        }

        edits.Reverse();
        output.AddRange(edits);
        return true;
    }
}
