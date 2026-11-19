namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanNodeViewModel
{
    [ObservableProperty]
    private bool _isIgnored;

    public bool CanIgnore => Space is not null && !IsRoot && !IsIgnored;

    public bool CanIgnoreSelection => IsMultiSelected
        ? _factory?.CanIgnoreSelected?.Invoke() == true
        : CanIgnore;

    public bool CanUnignoreSelection => IsMultiSelected
        ? _factory?.CanUnignoreSelected?.Invoke() == true
        : IsIgnored;

    public string UnignoreCaption => Space is { } space
        && _factory?.Visibility.IgnoredAncestor(space) is { } ancestor
        && !ReferenceEquals(ancestor, space)
            ? $"Снять игнорирование папки «{ancestor.Name}»"
            : "Снять игнорирование";

    public string EffectiveUnignoreCaption => IsMultiSelected ? "Снять игнорирование" : UnignoreCaption;

    public string IgnoreHint => IsIgnored ? "Игнорируется: размер не участвует в долях, подсветке и карте" : string.Empty;

    partial void OnIsIgnoredChanged(bool value)
    {
        OnPropertyChanged(nameof(CanIgnore));
        OnPropertyChanged(nameof(CanIgnoreSelection));
        OnPropertyChanged(nameof(CanUnignoreSelection));
        OnPropertyChanged(nameof(EffectiveUnignoreCaption));
    }

    [RelayCommand]
    private void Ignore()
    {
        if (CanIgnoreSelection)
        {
            _factory?.RequestIgnore(this);
        }
    }

    [RelayCommand]
    private void Unignore()
    {
        if (CanUnignoreSelection)
        {
            _factory?.RequestUnignore(this);
        }
    }
}
