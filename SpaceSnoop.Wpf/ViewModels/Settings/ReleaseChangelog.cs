using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpaceSnoop.Wpf.ViewModels.Settings;

internal static partial class ReleaseChangelog
{
    internal static string ExtractChanges(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var text = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string marker = "## Изменения";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (index >= 0)
        {
            text = text[(index + marker.Length)..].Trim();
            var next = text.IndexOf("\n## ", StringComparison.Ordinal);

            if (next >= 0)
            {
                text = text[..next].Trim();
            }
        }
        else
        {
            text = RemoveSection(text, "## Скачать");
            text = RemoveSection(text, "## Статистика");
        }

        return text.Replace("\n", Environment.NewLine, StringComparison.Ordinal).Trim();
    }

    internal static IReadOnlyList<ReleaseChangeViewModel> ExtractChangeItems(string? body)
    {
        var changes = ExtractChanges(body);

        if (string.IsNullOrWhiteSpace(changes))
        {
            return [];
        }

        var items = new List<(string Summary, List<string> Details)>();

        foreach (var line in changes.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("**Полный список:**", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                items.Add((line[2..].Trim(), []));
                continue;
            }

            var detail = line.Trim();

            if (detail.Length == 0)
            {
                continue;
            }

            if (items.Count == 0)
            {
                items.Add((detail, []));
            }
            else
            {
                items[^1].Details.Add(detail);
            }
        }

        return items.Select(static item => new ReleaseChangeViewModel(item.Summary, item.Details)).ToList();
    }

    internal static string BuildReleaseNotes(JsonElement releases)
    {
        var notes = new List<(string Tag, string Changes)>();

        foreach (var release in releases.EnumerateArray())
        {
            var tag = release.TryGetProperty("tag_name", out var tagProperty) ? tagProperty.GetString() : null;

            if (!UpdateCheck.IsNewer(tag, AppInfo.Version))
            {
                continue;
            }

            var body = release.TryGetProperty("body", out var bodyProperty) ? bodyProperty.GetString() : null;
            var changes = ExtractChanges(body).Replace("**", string.Empty, StringComparison.Ordinal);

            if (string.IsNullOrWhiteSpace(changes))
            {
                continue;
            }

            notes.Add((tag ?? string.Empty, changes));
        }

        return notes.Count == 1
            ? notes[0].Changes
            : string.Join($"{Environment.NewLine}{Environment.NewLine}", notes.Select(static note => $"{note.Tag}{Environment.NewLine}{note.Changes}"));
    }

    internal static IReadOnlyList<ReleaseNoteViewModel> BuildChangelogEntries(JsonElement releases)
    {
        var entries = new List<ReleaseNoteViewModel>();

        foreach (var release in releases.EnumerateArray())
        {
            if (BuildChangelogEntry(release) is { } entry)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    internal static string BuildChangelog(IReadOnlyList<ReleaseNoteViewModel> entries)
    {
        return string.Join($"{Environment.NewLine}{Environment.NewLine}", entries
            .Select(static entry =>
                $"## {entry.Title} · {entry.PublishedDate}{Environment.NewLine}{string.Join(Environment.NewLine, entry.Changes.Select(static change => FormatChange(change)))}"));
    }

    internal static string? ExtractCompareUrl(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var match = CompareUrlRegex().Match(body);
        return match.Success ? match.Value.TrimEnd('.') : null;
    }

    private static string RemoveSection(string text, string heading)
    {
        var start = text.IndexOf(heading, StringComparison.OrdinalIgnoreCase);

        if (start < 0)
        {
            return text;
        }

        var end = text.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return (end < 0 ? text[..start] : text[..start] + text[(end + 1)..]).Trim();
    }

    private static string FormatChange(ReleaseChangeViewModel change)
    {
        var lines = new List<string> { $"- {change.Summary}" };
        lines.AddRange(change.Details.Select(static detail => $"  {detail}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static ReleaseNoteViewModel? BuildChangelogEntry(JsonElement release)
    {
        var tag = StringProperty(release, "tag_name");
        var name = StringProperty(release, "name");
        var date = StringProperty(release, "published_at");
        var body = StringProperty(release, "body");
        var title = string.IsNullOrWhiteSpace(name) ? tag : name;

        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var publishedDate = string.IsNullOrWhiteSpace(date) || date.Length < 10 ? "без даты" : date[..10];
        var changes = ExtractChangeItems(body);
        var compareUrl = ExtractCompareUrl(body);

        return new(title, publishedDate, changes.Count == 0 ? [new("Изменения не описаны.", [])] : changes, compareUrl ?? StringProperty(release, "html_url"));
    }

    private static string? StringProperty(JsonElement owner, string name)
    {
        return owner.TryGetProperty(name, out var property) ? property.GetString() : null;
    }

    [GeneratedRegex(@"https://\S+/compare/\S+")]
    private static partial Regex CompareUrlRegex();
}

public sealed record ReleaseNoteViewModel(string Title, string PublishedDate, IReadOnlyList<ReleaseChangeViewModel> Changes, string? CompareUrl);

public sealed record ReleaseChangeViewModel(string Summary, IReadOnlyList<string> Details);
