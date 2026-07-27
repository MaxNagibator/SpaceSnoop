namespace SpaceSnoop.Wpf.ViewModels.Chat;

public sealed record ChatToolCall(string Name, string Text, bool IsMutating)
{
    public static ChatToolCall From(string? toolName)
    {
        var name = toolName ?? string.Empty;

        return new(name, AgentPrompt.Describe(name), AgentPrompt.IsDestructive(name));
    }
}
