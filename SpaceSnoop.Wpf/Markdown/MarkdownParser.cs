using System.Text;

namespace SpaceSnoop.Wpf.Markdown;

public static class MarkdownParser
{
    public const int MaxHeadingLevel = 6;

    public const int MaxListLevel = 2;

    private const string Fence = "```";

    public static IReadOnlyList<MarkdownBlock> Parse(string text)
    {
        var blocks = new List<MarkdownBlock>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return blocks;
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index];

            if (line.Trim().Length == 0)
            {
                index++;
                continue;
            }

            if (line.TrimStart().StartsWith(Fence, StringComparison.Ordinal))
            {
                blocks.Add(ReadCode(lines, ref index));
                continue;
            }

            if (TryReadHeading(line, out var heading))
            {
                blocks.Add(heading);
                index++;
                continue;
            }

            if (TryReadListItem(line, out var item))
            {
                blocks.Add(item);
                index++;
                continue;
            }

            blocks.Add(ReadParagraph(lines, ref index));
        }

        return blocks;
    }

    internal static IReadOnlyList<MarkdownSpan> ParseInline(string text)
    {
        var spans = new List<MarkdownSpan>();
        var buffer = new StringBuilder();
        var index = 0;

        while (index < text.Length)
        {
            var symbol = text[index];

            if (symbol == '`' && TryReadDelimited(text, index, "`", out var code, out var afterCode))
            {
                Flush(spans, buffer);
                spans.Add(new(code, MarkdownSpanStyle.Code));
                index = afterCode;
                continue;
            }

            if (symbol == '*' && index + 1 < text.Length && text[index + 1] == '*'
                && TryReadDelimited(text, index, "**", out var bold, out var afterBold))
            {
                Flush(spans, buffer);
                spans.AddRange(Nest(bold, MarkdownSpanStyle.Bold));
                index = afterBold;
                continue;
            }

            if (symbol == '*' && TryReadDelimited(text, index, "*", out var italic, out var afterItalic))
            {
                Flush(spans, buffer);
                spans.AddRange(Nest(italic, MarkdownSpanStyle.Italic));
                index = afterItalic;
                continue;
            }

            buffer.Append(symbol);
            index++;
        }

        Flush(spans, buffer);

        return spans;
    }

    private static IEnumerable<MarkdownSpan> Nest(string inner, MarkdownSpanStyle style)
    {
        return ParseInline(inner).Select(span => span with { Style = span.Style | style });
    }

    private static bool TryReadDelimited(string text, int start, string marker, out string inner, out int next)
    {
        inner = string.Empty;
        next = start;

        var contentStart = start + marker.Length;

        if (contentStart >= text.Length)
        {
            return false;
        }

        var close = text.IndexOf(marker, contentStart, StringComparison.Ordinal);

        if (close < 0 || close == contentStart)
        {
            return false;
        }

        inner = text[contentStart..close];
        next = close + marker.Length;

        return true;
    }

    private static void Flush(List<MarkdownSpan> spans, StringBuilder buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        spans.Add(new(buffer.ToString(), MarkdownSpanStyle.None));
        buffer.Clear();
    }

    private static MarkdownBlock ReadCode(string[] lines, ref int index)
    {
        var body = new List<string>();
        index++;

        while (index < lines.Length && !lines[index].TrimStart().StartsWith(Fence, StringComparison.Ordinal))
        {
            body.Add(lines[index]);
            index++;
        }

        if (index < lines.Length)
        {
            index++;
        }

        return new(MarkdownBlockKind.Code, [new(string.Join("\n", body), MarkdownSpanStyle.Code)]);
    }

    private static bool TryReadHeading(string line, out MarkdownBlock block)
    {
        block = new(MarkdownBlockKind.None, []);

        var trimmed = line.TrimStart();
        var level = 0;

        while (level < trimmed.Length && trimmed[level] == '#')
        {
            level++;
        }

        if (level == 0 || level > MaxHeadingLevel || level >= trimmed.Length || trimmed[level] != ' ')
        {
            return false;
        }

        block = new(MarkdownBlockKind.Heading, ParseInline(trimmed[(level + 1)..].Trim())) { Level = level };

        return true;
    }

    private static bool TryReadListItem(string line, out MarkdownBlock block)
    {
        block = new(MarkdownBlockKind.None, []);

        var trimmed = line.TrimStart();
        var indent = line.Length - trimmed.Length;
        var level = Math.Min(indent / 2, MaxListLevel);

        if (trimmed.Length > 1 && trimmed[0] is '-' or '*' or '+' && trimmed[1] == ' ')
        {
            block = new(MarkdownBlockKind.Bullet, ParseInline(trimmed[2..].Trim())) { Level = level };

            return true;
        }

        var digits = 0;

        while (digits < trimmed.Length && char.IsAsciiDigit(trimmed[digits]))
        {
            digits++;
        }

        if (digits == 0 || digits + 1 >= trimmed.Length || trimmed[digits] is not ('.' or ')') || trimmed[digits + 1] != ' ')
        {
            return false;
        }

        block = new(MarkdownBlockKind.Ordered, ParseInline(trimmed[(digits + 2)..].Trim()))
        {
            Level = level,
            Marker = trimmed[..digits],
        };

        return true;
    }

    private static MarkdownBlock ReadParagraph(string[] lines, ref int index)
    {
        var body = new List<string>();

        while (index < lines.Length)
        {
            var line = lines[index];

            if (line.Trim().Length == 0
                || line.TrimStart().StartsWith(Fence, StringComparison.Ordinal)
                || TryReadHeading(line, out _)
                || TryReadListItem(line, out _))
            {
                break;
            }

            body.Add(line.Trim());
            index++;
        }

        return new(MarkdownBlockKind.Paragraph, ParseInline(string.Join(" ", body)));
    }
}
