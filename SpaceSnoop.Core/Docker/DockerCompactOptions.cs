namespace SpaceSnoop.Core.Docker;

public sealed record DockerCompactOptions
{
    public TimeSpan ProcessTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan CompactTimeout { get; init; } = System.Threading.Timeout.InfiniteTimeSpan;

    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(45);

    public TimeSpan StatusPollInterval { get; init; } = TimeSpan.FromSeconds(2);
}
