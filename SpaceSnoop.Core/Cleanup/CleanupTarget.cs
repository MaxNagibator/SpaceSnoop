namespace SpaceSnoop.Core.Cleanup;

public sealed record CleanupTarget
{
    public static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromHours(24);

    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required CleanupTargetKind Kind { get; init; }

    public string Path { get; init; } = string.Empty;

    public string SearchPattern { get; init; } = "*";

    public bool Recursive { get; init; } = true;

    public bool Supported { get; init; } = true;

    public TimeSpan MinimumAge { get; init; } = DefaultMinimumAge;
}
