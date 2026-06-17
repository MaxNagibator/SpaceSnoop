namespace SpaceSnoop.Core.Docker;

public sealed record DockerSnapshot(bool Available, IReadOnlyList<DockerUsage> Buckets, string? Error)
{
    public static DockerSnapshot Unavailable(string error)
    {
        return new(false, [], error);
    }

    public static DockerSnapshot Ok(IReadOnlyList<DockerUsage> buckets)
    {
        return new(true, buckets, null);
    }
}
