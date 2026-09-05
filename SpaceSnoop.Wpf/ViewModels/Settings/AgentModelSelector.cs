using System.ComponentModel;

namespace SpaceSnoop.Wpf.ViewModels.Settings;

public sealed partial class AgentModelSelector : ObservableObject
{
    private readonly AgentPreferences _preferences;
    private readonly AgentBackends _backends;
    private readonly ILogger<AgentModelSelector> _logger;

    private readonly Dictionary<AgentBackendKind, IReadOnlyList<AgentModelOption>> _catalogs = [];

    private bool _suppressSync;

    [ObservableProperty]
    private IReadOnlyList<AgentModelOption> _modelOptions = [];

    [ObservableProperty]
    private AgentModelOption _selectedModel = AgentModels.CliDefault;

    [ObservableProperty]
    private IReadOnlyList<AgentEffortOption> _effortOptions = [];

    [ObservableProperty]
    private AgentEffortOption _selectedEffort = AgentModels.EffortDefault;

    [ObservableProperty]
    private bool _isLoadingModels;

    public AgentModelSelector(AgentPreferences preferences, AgentBackends backends, ILogger<AgentModelSelector> logger)
    {
        _preferences = preferences;
        _backends = backends;
        _logger = logger;
        _preferences.PropertyChanged += OnPreferencesChanged;
        Sync();
    }

    public Task EnsureModelsAsync()
    {
        return _catalogs.ContainsKey(_preferences.Backend) ? Task.CompletedTask : LoadModelsAsync();
    }

    [RelayCommand]
    private async Task ReloadModelsAsync()
    {
        await LoadModelsAsync();
    }

    private async Task LoadModelsAsync()
    {
        var backend = _backends.Current;
        IsLoadingModels = true;

        try
        {
            var models = await Task.Run(backend.LoadModels);

            _catalogs[backend.Kind] = models;
            _logger.AgentModelsLoaded(backend.DisplayName, models.Count);
        }
        catch (Exception exception)
        {
            _logger.AgentModelsFailed(exception, backend.DisplayName);
        }
        finally
        {
            IsLoadingModels = false;
        }

        if (_preferences.Backend == backend.Kind)
        {
            Sync();
        }
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

        List<AgentModelOption> models = [AgentModels.CliDefault, .. Catalog(backend)];

        if (model.Length > 0 && !models.Exists(option => string.Equals(option.Id, model, StringComparison.OrdinalIgnoreCase)))
        {
            models.Add(new AgentModelOption(model, model, "Задана вручную"));
        }

        ModelOptions = models;
        SelectedModel = models.Find(option => string.Equals(option.Id, model, StringComparison.OrdinalIgnoreCase)) ?? AgentModels.CliDefault;

        var efforts = AgentModels.EffortsFor(SelectedModel, backend);
        var effort = _preferences.Effort.Trim();

        EffortOptions = efforts;
        SelectedEffort = efforts.FirstOrDefault(option => string.Equals(option.Id, effort, StringComparison.OrdinalIgnoreCase)) ?? AgentModels.EffortDefault;

        if (!string.Equals(SelectedEffort.Id, effort, StringComparison.Ordinal))
        {
            _preferences.Effort = SelectedEffort.Id;
        }

        _suppressSync = false;
    }

    private IReadOnlyList<AgentModelOption> Catalog(AgentBackendKind backend)
    {
        return _catalogs.TryGetValue(backend, out var loaded) ? loaded : AgentModels.For(backend);
    }
}
