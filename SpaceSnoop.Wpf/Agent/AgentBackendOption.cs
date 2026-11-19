namespace SpaceSnoop.Wpf.Agent;

public sealed record AgentBackendOption(AgentBackendKind Kind, string Title, string Hint)
{
    public override string ToString()
    {
        return Title;
    }
}
