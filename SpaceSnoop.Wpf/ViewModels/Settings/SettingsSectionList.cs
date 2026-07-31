namespace SpaceSnoop.Wpf.ViewModels.Settings;

public sealed partial class SettingsSectionList : ObservableObject
{
    private readonly Dictionary<string, SettingsSection> _byKey;

    [ObservableProperty]
    private SettingsSection? _selected;

    public SettingsSectionList(params SettingsSection[] sections)
    {
        Items = sections;
        _byKey = sections.ToDictionary(section => section.Key, StringComparer.Ordinal);
        Selected = sections.FirstOrDefault();
    }

    public IReadOnlyList<SettingsSection> Items { get; }

    public bool NoMatches => Items.All(section => !section.IsVisible);

    public SettingsSection this[string key] => _byKey[key];

    public void Restore(string? key)
    {
        if (!string.IsNullOrEmpty(key) && _byKey.TryGetValue(key, out var section))
        {
            Selected = section;
        }
    }

    public void Filter(string? query)
    {
        var terms = SettingsSection.ParseQuery(query);

        foreach (var section in Items)
        {
            section.IsVisible = section.Matches(terms);
        }

        if (Selected is null || !Selected.IsVisible)
        {
            Selected = Items.FirstOrDefault(section => section.IsVisible);
        }

        OnPropertyChanged(nameof(NoMatches));
    }

    partial void OnSelectedChanged(SettingsSection? oldValue, SettingsSection? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    [RelayCommand]
    private void Select(SettingsSection section)
    {
        Selected = section;
    }
}
