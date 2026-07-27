namespace SpaceSnoop.Wpf.ViewModels.Chat;

public sealed record ChatToolCall(string Name, string Text, bool IsMutating, string Arguments)
{
    public string Details => ChatToolArguments.Describe(Arguments);

    public static ChatToolCall From(string? toolName, string? arguments = null)
    {
        var name = toolName ?? string.Empty;

        return new(name, AgentPrompt.Describe(name), AgentPrompt.IsDestructive(name), arguments ?? string.Empty);
    }
}
