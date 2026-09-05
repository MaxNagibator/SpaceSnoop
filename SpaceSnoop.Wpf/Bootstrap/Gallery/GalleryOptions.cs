namespace SpaceSnoop.Wpf.Bootstrap.Gallery;

public sealed record GalleryOptions(
    GalleryArguments Arguments,
    IReadOnlyList<string> Dialogs,
    IReadOnlyList<string> Tips,
    string State)
{
    public const string NoneValue = "none";
    public const string DialogsKey = "--dialogs";
    public const string TipsKey = "--tips";
    public const string StateKey = "--state";

    public static IReadOnlyList<string> AllPages { get; } = [.. SectionKey.All];

    public string Directory => Arguments.Directory;

    public IReadOnlyList<string> Pages => Arguments.Pages;

    public IReadOnlyList<string> Themes => Arguments.Themes;

    public static IReadOnlyList<string> DefaultThemes { get; } = [AppThemes.LightKey, AppThemes.DarkKey];

    public static GalleryOptions Parse(IEnumerable<string> args, string defaultDirectory)
    {
        var defaults = new GalleryDefaults
        {
            Directory = defaultDirectory,
            Themes = DefaultThemes,
            Width = AppDefaults.GalleryWidthDefault,
            Height = AppDefaults.GalleryHeightDefault,
            SizeMin = AppDefaults.GallerySizeMin,
            SizeMax = AppDefaults.GallerySizeMax,
            Scale = AppDefaults.ViewCaptureScaleDefault,
            ThemeDelayMs = AppDefaults.GalleryThemeDelayMs,
            FrameDelayMs = AppDefaults.GalleryFrameDelayMs,
        };

        var arguments = GalleryArguments.Parse(args, defaults, ResolvePages, [DialogsKey, TipsKey, StateKey]);

        var unknown = new List<string>();
        var dialogs = ParseSet(arguments.Extra(DialogsKey), GalleryDialogs.All, unknown);
        var tips = ParseSet(arguments.Extra(TipsKey), GalleryTips.All, unknown);
        var state = ParseState(arguments.Extra(StateKey), unknown);

        if (unknown.Count > 0)
        {
            arguments = arguments with { Unknown = [.. arguments.Unknown, .. unknown] };
        }

        return new(arguments, dialogs, tips, state);
    }

    private static IReadOnlyList<string> ResolvePages(IReadOnlyList<string> requested, ICollection<string> unknown)
    {
        if (requested.Count == 0)
        {
            return AllPages;
        }

        var pages = new List<string>();

        foreach (var page in requested)
        {
            if (SectionKey.Match(page) is not { } match)
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

    private static IReadOnlyList<string> ParseSet(string value, IReadOnlyList<string> all, List<string> unknown)
    {
        var requested = GalleryArguments.Split(value);

        if (requested.Count == 1 && string.Equals(requested[0], NoneValue, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var chosen = new List<string>();

        foreach (var item in requested)
        {
            var match = all.FirstOrDefault(known => string.Equals(known, item, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                unknown.Add(item);
                continue;
            }

            if (!chosen.Contains(match, StringComparer.Ordinal))
            {
                chosen.Add(match);
            }
        }

        return chosen.Count > 0 ? chosen : all;
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
}
