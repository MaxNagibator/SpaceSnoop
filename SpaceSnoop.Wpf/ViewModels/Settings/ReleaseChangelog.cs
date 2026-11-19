using MahApps.Metro.IconPacks;
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

    internal static IReadOnlyList<ReleaseChangeGroupViewModel> ExtractChangeGroups(string? body)
    {
        var changes = ExtractChanges(body);

        if (string.IsNullOrWhiteSpace(changes))
        {
            return [];
        }

        var groups = new List<(string? Title, List<(string Summary, List<string> Details)> Items)>();
        string? currentTitle = null;
        var currentItems = new List<(string Summary, List<string> Details)>();

        void FlushGroup()
        {
            if (currentItems.Count > 0)
            {
                groups.Add((currentTitle, currentItems));
                currentItems = [];
            }
        }

        foreach (var line in changes.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("**Полный список:**", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                var heading = line[4..].Trim();

                if (heading.Length > 0)
                {
                    FlushGroup();
                    currentTitle = heading;
                }

                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                currentItems.Add((line[2..].Trim(), []));
                continue;
            }

            var detail = line.Trim();

            if (detail.Length == 0)
            {
                continue;
            }

            if (currentItems.Count == 0)
            {
                currentItems.Add((detail, []));
            }
            else
            {
                currentItems[^1].Details.Add(detail);
            }
        }

        FlushGroup();

        return groups
            .Select(static group => new ReleaseChangeGroupViewModel(
                group.Title,
                group.Items.Select(item => new ReleaseChangeViewModel(item.Summary, item.Details)).ToList()))
            .ToList();
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

    internal static IReadOnlyList<ReleaseNoteViewModel> BuildPendingEntries(JsonElement releases, string? currentVersion = null)
    {
        var current = currentVersion ?? AppInfo.Version;
        var entries = new List<ReleaseNoteViewModel>();

        foreach (var release in releases.EnumerateArray())
        {
            var tag = StringProperty(release, "tag_name");

            if (!UpdateCheck.IsNewer(tag, current))
            {
                continue;
            }

            if (BuildChangelogEntry(release) is { } entry)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    internal static string BuildChangelog(IReadOnlyList<ReleaseNoteViewModel> entries)
    {
        return string.Join(
            $"{Environment.NewLine}{Environment.NewLine}",
            entries
                .Select(static entry =>
                    $"## {entry.Title} · {entry.PublishedDate}{Environment.NewLine}{string.Join(Environment.NewLine, entry.ChangeGroups.Select(static group => FormatGroup(group)))}"));
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

    internal static IReadOnlyList<ConfirmLine> BuildUpdateLines(IReadOnlyList<ReleaseNoteViewModel> entries, string fallbackNotes)
    {
        var lines = new List<ConfirmLine>();

        if (entries.Count > 0)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                if (index > 0)
                {
                    lines.Add(new ConfirmGapLine());
                }

                var entry = entries[index];

                if (entries.Count > 1)
                {
                    lines.Add(new ConfirmTextLine($"{entry.Title} · {entry.PublishedDate}", ConfirmTextTone.Strong));
                }

                AppendGroups(lines, entry.ChangeGroups);
            }

            return lines;
        }

        var groups = ExtractChangeGroups(fallbackNotes);

        if (groups.Count > 0)
        {
            AppendGroups(lines, groups);
            return lines;
        }

        lines.Add(new ConfirmTextLine("Список изменений не указан.", ConfirmTextTone.Muted));
        return lines;
    }

    internal static ConfirmDialogViewModel BuildUpdateDialog(
        string? latestTag,
        string? assetName,
        string? assetUrl,
        IReadOnlyList<ReleaseNoteViewModel> entries,
        string fallbackNotes)
    {
        var hasAsset = assetUrl is not null && !string.IsNullOrWhiteSpace(assetName);
        var summary = hasAsset
            ? $"Доступна версия {latestTag} (установлена {AppInfo.Version}). Файл «{assetName}» будет сохранён рядом с программой."
            : $"Доступна версия {latestTag} (установлена {AppInfo.Version}).";

        IReadOnlyList<ConfirmChoice> choices = hasAsset
            ?
            [
                new("Не сейчас", ConfirmChoiceKind.Dismissive),
                new("Открыть на GitHub", ConfirmChoiceKind.Secondary),
                new("Скачать", ConfirmChoiceKind.Primary),
            ]
            :
            [
                new("Не сейчас", ConfirmChoiceKind.Dismissive),
                new("Открыть на GitHub", ConfirmChoiceKind.Primary),
            ];

        return new(
            "Доступно обновление",
            PackIconLucideKind.Download,
            BuildUpdateLines(entries, fallbackNotes),
            choices)
        {
            Summary = summary,
        };
    }

    internal static ConfirmDialogViewModel BuildDownloadedDialog(string targetPath)
    {
        return new(
            "Обновление скачано",
            PackIconLucideKind.Download,
            [
                new ConfirmTextLine("Файл сохранён рядом с программой:"),
                new ConfirmTextLine(targetPath, ConfirmTextTone.Muted),
            ],
            [
                new("Не сейчас", ConfirmChoiceKind.Dismissive),
                new("Показать в проводнике", ConfirmChoiceKind.Primary),
            ]);
    }

    private static void AppendGroups(List<ConfirmLine> lines, IReadOnlyList<ReleaseChangeGroupViewModel> groups)
    {
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];

            if (group.HasTitle)
            {
                if (groupIndex > 0)
                {
                    lines.Add(new ConfirmGapLine());
                }

                lines.Add(new ConfirmTextLine(group.Title!, ConfirmTextTone.Strong));
            }

            foreach (var item in group.Items)
            {
                lines.Add(new ConfirmBulletLine(item.Summary, item.Details));
            }
        }
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

    private static string FormatGroup(ReleaseChangeGroupViewModel group)
    {
        var lines = new List<string>();

        if (group.HasTitle)
        {
            lines.Add($"### {group.Title}");
        }

        lines.AddRange(group.Items.Select(static change => FormatChange(change)));
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatChange(ReleaseChangeViewModel change)
    {
        var lines = new List<string>
        {
            $"- {change.Summary}",
        };

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
        var groups = ExtractChangeGroups(body);
        var changes = groups.SelectMany(static group => group.Items).ToList();

        if (changes.Count == 0)
        {
            changes = [new("Изменения не описаны.", [])];
            groups = [new(null, changes)];
        }

        var compareUrl = ExtractCompareUrl(body);

        return new(title, publishedDate, changes, compareUrl ?? StringProperty(release, "html_url"), groups);
    }

    private static string? StringProperty(JsonElement owner, string name)
    {
        return owner.TryGetProperty(name, out var property) ? property.GetString() : null;
    }

    [GeneratedRegex(@"https://\S+/compare/\S+")]
    private static partial Regex CompareUrlRegex();
}

public sealed record ReleaseNoteViewModel(
    string Title,
    string PublishedDate,
    IReadOnlyList<ReleaseChangeViewModel> Changes,
    string? CompareUrl,
    IReadOnlyList<ReleaseChangeGroupViewModel>? Groups = null)
{
    public IReadOnlyList<ReleaseChangeGroupViewModel> ChangeGroups =>
        Groups is { Count: > 0 } ? Groups : [new(null, Changes)];
}

public sealed record ReleaseChangeGroupViewModel(string? Title, IReadOnlyList<ReleaseChangeViewModel> Items)
{
    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
}

public sealed record ReleaseChangeViewModel(string Summary, IReadOnlyList<string> Details);
