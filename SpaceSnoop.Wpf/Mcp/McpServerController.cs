using System.ComponentModel;

namespace SpaceSnoop.Wpf.Mcp;

public sealed class McpServerController : ObservableObject, IDisposable
{
    private readonly McpPreferences _preferences;
    private readonly Lazy<McpServerHost> _host;
    private bool _disposed;

    public McpServerController(McpPreferences preferences, Func<McpServerHost> createHost)
    {
        _preferences = preferences;
        _host = new(() =>
        {
            var host = createHost();
            host.PropertyChanged += OnHostChanged;
            return host;
        });
        _preferences.PropertyChanged += OnPreferencesChanged;
    }

    internal McpServerHost? CreatedHost => _host.IsValueCreated ? _host.Value : null;

    public string? Endpoint => CreatedHost?.Endpoint;

    public string? LastError => CreatedHost?.LastError;

    public bool IsRunning => CreatedHost?.IsRunning == true;

    public void Apply()
    {
        if (!_disposed && (_preferences.Enabled || _host.IsValueCreated))
        {
            _host.Value.Apply();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _preferences.PropertyChanged -= OnPreferencesChanged;
        if (CreatedHost is { } host)
        {
            host.PropertyChanged -= OnHostChanged;
        }
    }

    private void OnHostChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(e);

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(McpPreferences.Enabled) or nameof(McpPreferences.Port) or nameof(McpPreferences.Token))
        {
            Apply();
        }
    }
}
