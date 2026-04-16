namespace SpaceSnoop.Wpf.ViewModels;

public sealed class ThemeViewModel(ISettingsStore store) : ThemeViewModelBase<AppTheme>(store)
{
    public bool IsLight => Current == AppTheme.Light;
    public bool IsDark => Current == AppTheme.Dark;
    public bool IsTarkov => Current == AppTheme.Tarkov;

    protected override string ToKey(AppTheme theme)
    {
        return AppThemes.ToKey(theme);
    }

    protected override AppTheme FromKey(string? key)
    {
        return AppThemes.FromKey(key);
    }

    protected override void OnThemeFlavorChanged(AppTheme value)
    {
        OnPropertyChanged(nameof(IsLight));
        OnPropertyChanged(nameof(IsDark));
        OnPropertyChanged(nameof(IsTarkov));
    }
}
