namespace SpaceSnoop.Wpf.ViewModels.Settings;

public sealed partial class SettingsSection : ObservableObject
{
    private readonly bool _expandedByDefault;
    private readonly string _haystack;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isVisible = true;

    public SettingsSection(string title, bool expandedByDefault, string keywords)
    {
        Title = title;
        _expandedByDefault = expandedByDefault;
        _isExpanded = expandedByDefault;
        _haystack = $"{title} {keywords}".ToLowerInvariant();
    }

    public string Title { get; }

    public static string[] ParseQuery(string? query)
    {
        return (query ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.ToLowerInvariant())
            .ToArray();
    }

    public void Filter(IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            IsVisible = true;
            IsExpanded = _expandedByDefault;
            return;
        }

        IsVisible = terms.All(term => _haystack.Contains(term, StringComparison.Ordinal));
        IsExpanded = IsVisible;
    }
}
