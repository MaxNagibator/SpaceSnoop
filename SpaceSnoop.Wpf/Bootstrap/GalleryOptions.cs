namespace SpaceSnoop.Wpf.Bootstrap;

public sealed record GalleryOptions(
    string Directory,
    IReadOnlyList<string> Pages,
    IReadOnlyList<string> Dialogs,
    IReadOnlyList<string> Tips,
    IReadOnlyList<AppTheme> Themes,
    int Width,
    int Height,
    double Scale,
    double FontScale,
    string Element,
    string State,
    IReadOnlyList<string> Unknown)
{
    public const string NoneValue = "none";

    public static IReadOnlyList<string> AllPages { get; } = [.. SectionKey.All];

    public static GalleryOptions Parse(IEnumerable<string> args, string defaultDirectory)
    {
        var directory = defaultDirectory;
        var pages = AllPages;
        var dialogs = GalleryDialogs.All;
        var tips = GalleryTips.All;
        IReadOnlyList<AppTheme> themes = [AppTheme.Light, AppTheme.Dark];
        var width = AppDefaults.GalleryWidthDefault;
        var height = AppDefaults.GalleryHeightDefault;
        var scale = AppDefaults.ViewCaptureScaleDefault;
        var fontScale = FontScaleManager.DefaultScale;
        var element = string.Empty;
        var state = GalleryStates.Idle;
        var unknown = new List<string>();

        var rest = args.ToList();
        var positional = true;

        var index = 0;

        while (index < rest.Count)
        {
            var key = rest[index].Trim();
            var value = index + 1 < rest.Count ? rest[index + 1].Trim() : string.Empty;
            index++;

            switch (key.ToLowerInvariant())
            {
                case "--pages":
                    pages = ParsePages(value, unknown);
                    index++;
                    positional = false;
                    break;

                case "--dialogs":
                    dialogs = ParseDialogs(value, unknown);
                    index++;
                    positional = false;
                    break;

                case "--tips":
                    tips = ParseTips(value, unknown);
                    index++;
                    positional = false;
                    break;

                case "--themes":
                    themes = ParseThemes(value, unknown);
                    index++;
                    positional = false;
                    break;

                case "--size":
                    (width, height) = ParseSize(value, width, height);
                    index++;
                    positional = false;
                    break;

                case "--element":
                    element = value;
                    index++;
                    positional = false;
                    break;

                case "--state":
                    state = ParseState(value, unknown);
                    index++;
                    positional = false;
                    break;

                case "--scale":
                    scale = ParseScale(value, scale);
                    index++;
                    positional = false;
                    break;

                case "--font-scale":
                    fontScale = ParseFontScale(value, fontScale);
                    index++;
                    positional = false;
                    break;

                default:
                    if (positional && key.Length > 0 && !key.StartsWith('-'))
                    {
                        directory = key;
                    }
                    else if (key.Length > 0)
                    {
                        unknown.Add(key);
                    }

                    positional = false;
                    break;
            }
        }

        return new(directory, pages, dialogs, tips, themes, width, height, scale, fontScale, element, state, unknown);
    }

    private static string ParseState(string value, List<string> unknown)
    {
        if (GalleryStates.Match(value) is { } known)
        {
            return known;
        }

        if (value.Length > 0)
        {
            unknown.Add(value);
        }

        return GalleryStates.Idle;
    }

    private static double ParseScale(string value, double scale)
    {
        return double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, AppDefaults.ViewCaptureScaleMin, AppDefaults.ViewCaptureScaleMax)
            : scale;
    }

    private static double ParseFontScale(string value, double fontScale)
    {
        return double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? FontScaleManager.Clamp(parsed)
            : fontScale;
    }

    private static IReadOnlyList<string> ParsePages(string value, List<string> unknown)
    {
        var requested = Split(value);

        if (requested.Count == 0)
        {
            return AllPages;
        }

        var pages = new List<string>();

        foreach (var page in requested)
        {
            var match = SectionKey.Match(page);

            if (match is null)
            {
                unknown.Add(page);
                continue;
            }

            if (!pages.Contains(match, StringComparer.Ordinal))
            {
                pages.Add(match);
            }
        }

        return pages.Count > 0 ? pages : AllPages;
    }

    private static IReadOnlyList<string> ParseDialogs(string value, List<string> unknown)
    {
        var requested = Split(value);

        if (requested.Count == 1 && string.Equals(requested[0], NoneValue, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var dialogs = new List<string>();

        foreach (var dialog in requested)
        {
            var match = GalleryDialogs.All.FirstOrDefault(known => string.Equals(known, dialog, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                unknown.Add(dialog);
                continue;
            }

            if (!dialogs.Contains(match, StringComparer.Ordinal))
            {
                dialogs.Add(match);
            }
        }

        return dialogs.Count > 0 ? dialogs : GalleryDialogs.All;
    }

    private static IReadOnlyList<string> ParseTips(string value, List<string> unknown)
    {
        var requested = Split(value);

        if (requested.Count == 1 && string.Equals(requested[0], NoneValue, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var tips = new List<string>();

        foreach (var tip in requested)
        {
            var match = GalleryTips.All.FirstOrDefault(known => string.Equals(known, tip, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                unknown.Add(tip);
                continue;
            }

            if (!tips.Contains(match, StringComparer.Ordinal))
            {
                tips.Add(match);
            }
        }

        return tips.Count > 0 ? tips : GalleryTips.All;
    }

    private static IReadOnlyList<AppTheme> ParseThemes(string value, List<string> unknown)
    {
        var themes = new List<AppTheme>();

        foreach (var name in Split(value))
        {
            var key = name.ToLowerInvariant();

            if (key is not (AppThemes.LightKey or AppThemes.DarkKey or AppThemes.TarkovKey))
            {
                unknown.Add(name);
                continue;
            }

            var theme = AppThemes.FromKey(key);

            if (!themes.Contains(theme))
            {
                themes.Add(theme);
            }
        }

        return themes.Count > 0 ? themes : [AppTheme.Light, AppTheme.Dark];
    }

    private static (int Width, int Height) ParseSize(string value, int width, int height)
    {
        var parts = value.ToLowerInvariant().Split('x', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length != 2 || !int.TryParse(parts[0], out var parsedWidth) || !int.TryParse(parts[1], out var parsedHeight))
        {
            return (width, height);
        }

        return (Clamp(parsedWidth), Clamp(parsedHeight));
    }

    private static int Clamp(int value)
    {
        return Math.Clamp(value, AppDefaults.GallerySizeMin, AppDefaults.GallerySizeMax);
    }

    private static List<string> Split(string value)
    {
        return [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }
}
