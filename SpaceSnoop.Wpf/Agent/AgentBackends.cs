namespace SpaceSnoop.Wpf.Agent;

public sealed class AgentBackends
{
    private readonly AgentPreferences _preferences;

    public AgentBackends(AgentPreferences preferences, ClaudeAgentBackend claude, CodexAgentBackend codex)
    {
        _preferences = preferences;
        All = [claude, codex];
    }

    public IReadOnlyList<IAgentBackend> All { get; }

    public IAgentBackend Current => All.FirstOrDefault(backend => backend.Kind == _preferences.Backend) ?? All[0];
}
