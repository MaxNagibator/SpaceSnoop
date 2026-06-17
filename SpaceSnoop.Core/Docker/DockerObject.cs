namespace SpaceSnoop.Core.Docker;

public sealed record DockerObject(
    DockerObjectKind Kind,
    string Id,
    string Name,
    string Size,
    long SizeBytes,
    bool InUse,
    string Detail);
