using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Settings;

public sealed partial class SettingsSection : ObservableObject
{
    private readonly string _haystack;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isVisible = true;

    public SettingsSection(string key, string title, PackIconLucideKind icon, string keywords)
    {
        Key = key;
        Title = title;
        Icon = icon;
        _haystack = $"{title} {keywords}".ToLowerInvariant();
    }

    public string Key { get; }

    public string Title { get; }

    public PackIconLucideKind Icon { get; }

    public static string[] ParseQuery(string? query)
    {
        return (query ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.ToLowerInvariant())
            .ToArray();
    }

    public bool Matches(IReadOnlyList<string> terms)
    {
        return terms.All(term => _haystack.Contains(term, StringComparison.Ordinal));
    }
}
