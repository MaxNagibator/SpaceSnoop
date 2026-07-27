using System.ComponentModel;

namespace SpaceSnoop.Wpf.ViewModels.Settings;

public sealed partial class AgentModelSelector : ObservableObject
{
    private readonly AgentPreferences _preferences;
    private bool _suppressSync;

    [ObservableProperty]
    private IReadOnlyList<AgentModelOption> _modelOptions = [];

    [ObservableProperty]
    private AgentModelOption _selectedModel = AgentModels.CliDefault;

    [ObservableProperty]
    private IReadOnlyList<AgentEffortOption> _effortOptions = [];

    [ObservableProperty]
    private AgentEffortOption _selectedEffort = AgentModels.EffortDefault;

    public AgentModelSelector(AgentPreferences preferences)
    {
        _preferences = preferences;
        _preferences.PropertyChanged += OnPreferencesChanged;
        Sync();
    }

    partial void OnSelectedModelChanged(AgentModelOption value)
    {
        if (!_suppressSync)
        {
            _preferences.Model = value.Id;
        }
    }

    partial void OnSelectedEffortChanged(AgentEffortOption value)
    {
        if (!_suppressSync)
        {
            _preferences.Effort = value.Id;
        }
    }

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressSync)
        {
            return;
        }

        if (e.PropertyName is nameof(AgentPreferences.Backend) or nameof(AgentPreferences.Model) or nameof(AgentPreferences.Effort))
        {
            Sync();
        }
    }

    private void Sync()
    {
        _suppressSync = true;

        var backend = _preferences.Backend;
        var model = _preferences.Model.Trim();

        List<AgentModelOption> models = [AgentModels.CliDefault, .. AgentModels.For(backend)];

        if (model.Length > 0 && AgentModels.Find(backend, model) is null)
        {
            models.Add(new AgentModelOption(model, model, "Задана вручную"));
        }

        ModelOptions = models;
        SelectedModel = models.Find(option => string.Equals(option.Id, model, StringComparison.OrdinalIgnoreCase)) ?? AgentModels.CliDefault;

        var efforts = AgentModels.Efforts(backend, model);
        var effort = _preferences.Effort.Trim();

        EffortOptions = efforts;
        SelectedEffort = efforts.FirstOrDefault(option => string.Equals(option.Id, effort, StringComparison.OrdinalIgnoreCase)) ?? AgentModels.EffortDefault;

        if (!string.Equals(SelectedEffort.Id, effort, StringComparison.Ordinal))
        {
            _preferences.Effort = SelectedEffort.Id;
        }

        _suppressSync = false;
    }
}
