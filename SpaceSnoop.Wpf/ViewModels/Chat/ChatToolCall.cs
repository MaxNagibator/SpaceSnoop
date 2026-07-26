namespace SpaceSnoop.Wpf.ViewModels.Chat;

public sealed record ChatToolCall(string Text, bool IsMutating)
{
    public static ChatToolCall From(string? toolName)
    {
        var name = toolName ?? string.Empty;

        return new(AgentPrompt.Describe(name), AgentPrompt.IsDestructive(name));
    }
}
