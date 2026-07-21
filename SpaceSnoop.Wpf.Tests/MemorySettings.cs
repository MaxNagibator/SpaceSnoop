using KeepShell.Bootstrap;

namespace SpaceSnoop.Wpf.Tests;

internal sealed class MemorySettings : ISettingsStore
{
    private readonly Dictionary<string, string> _values = [];

    public event EventHandler<string>? Changed;

    public string FilePath => string.Empty;

    public string? GetStringValue(string key)
    {
        return _values.GetValueOrDefault(key);
    }

    public void SetValue(string key, string value)
    {
        _values[key] = value;
        Changed?.Invoke(this, key);
    }

    public void Flush()
    {
    }
}
