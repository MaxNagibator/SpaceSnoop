namespace SpaceSnoop.Wpf.ViewModels.Settings;

public sealed class SettingsResetField
{
    private readonly Func<bool> _isDefault;
    private readonly Action _apply;

    public SettingsResetField(
        string key,
        string label,
        string defaultText,
        Func<bool> isDefault,
        Action apply,
        Action<SettingsResetField> onReset)
    {
        ArgumentNullException.ThrowIfNull(isDefault);
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(onReset);

        Key = key;
        Label = label;
        DefaultText = defaultText;
        _isDefault = isDefault;
        _apply = apply;

        ResetCommand = new RelayCommand(() => onReset(this), () => !_isDefault());
    }

    public string Key { get; }

    public string Label { get; }

    public string DefaultText { get; }

    public IRelayCommand ResetCommand { get; }

    public bool IsDefault => _isDefault();

    public string Hint => $"Вернуть к заводскому значению: {DefaultText}";

    public string AutomationName => $"Сбросить: {Label}";

    public void Apply()
    {
        _apply();
    }

    public void RefreshAvailability()
    {
        ResetCommand.NotifyCanExecuteChanged();
    }
}
