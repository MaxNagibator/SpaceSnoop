namespace SpaceSnoop.Wpf.ViewModels.Chat;

public sealed record ChatHistoryDocument
{
    public int Version { get; init; } = 1;

    public List<ChatConversationRecord> Conversations { get; init; } = [];
}

public sealed record ChatConversationRecord
{
    public required string Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public AgentBackendKind Backend { get; init; }

    public string? SessionId { get; init; }

    public DateTimeOffset StartedUtc { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }

    public List<ChatMessageRecord> Messages { get; init; } = [];
}

public sealed record ChatMessageRecord
{
    public ChatRole Role { get; init; }

    public string Text { get; init; } = string.Empty;

    public bool IsError { get; init; }

    public bool IsCancelled { get; init; }

    public double CostUsd { get; init; }

    public long Tokens { get; init; }

    public List<string> Tools { get; init; } = [];
}
