namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class OperationPreferences : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly bool _suppressPersist;

    [ObservableProperty]
    private bool _confirmBeforeDelete = AppDefaults.DeleteConfirmDefault;

    [ObservableProperty]
    private DeleteMode _deleteMode = AppDefaults.DeleteModeDefault;

    [ObservableProperty]
    private string _defaultExclusions = string.Empty;

    public OperationPreferences(ISettingsStore settings)
    {
        _settings = settings;

        _suppressPersist = true;
        ConfirmBeforeDelete = _settings.GetBool(SettingsKeys.DeleteConfirm, AppDefaults.DeleteConfirmDefault);
        DeleteMode = _settings.GetEnum(SettingsKeys.DeleteMode, AppDefaults.DeleteModeDefault);
        DefaultExclusions = _settings.GetStringValue(SettingsKeys.DefaultExclusions) ?? string.Empty;
        _suppressPersist = false;
    }

    partial void OnConfirmBeforeDeleteChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.DeleteConfirm, value);
        }
    }

    partial void OnDeleteModeChanged(DeleteMode value)
    {
        if (!_suppressPersist)
        {
            _settings.SetEnum(SettingsKeys.DeleteMode, value);
        }
    }

    partial void OnDefaultExclusionsChanged(string value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(SettingsKeys.DefaultExclusions, value);
        }
    }
}
