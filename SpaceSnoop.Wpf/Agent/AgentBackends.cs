namespace SpaceSnoop.Wpf.Agent;

public sealed class AgentBackends
{
    private readonly AgentPreferences _preferences;

    public AgentBackends(AgentPreferences preferences, ClaudeAgentBackend claude, CodexAgentBackend codex, OpenCodeAgentBackend openCode)
    {
        _preferences = preferences;
        All = [claude, codex, openCode];
    }

    public IReadOnlyList<IAgentBackend> All { get; }

    public IAgentBackend Current => All.FirstOrDefault(backend => backend.Kind == _preferences.Backend) ?? All[0];
}
