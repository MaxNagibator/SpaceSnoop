using KeepShell.Diagnostics;

namespace SpaceSnoop.Wpf.Diagnostics;

public sealed class SpaceSnoopSecretSource(ISettingsStore settings) : IDiagnosticsSecretSource
{
    private static readonly string[] KeyNames = ["token", "secret", "password", "credential", "apikey", "api_key", "api-key"];

    public DiagnosticsSecretRules Collect()
    {
        var values = new List<string>(1);

        if (settings.GetStringValue(SettingsKeys.McpToken)?.Trim() is { Length: > 0 } token)
        {
            values.Add(token);
        }

        return new(KeyNames, values);
    }
}
