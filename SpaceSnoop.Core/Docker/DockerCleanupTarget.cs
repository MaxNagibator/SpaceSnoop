namespace SpaceSnoop.Core.Docker;

public enum DockerCleanupTarget
{
    None = 0,
    BuildCache = 1,
    DanglingImages = 2,
    UnusedImages = 3,
    StoppedContainers = 4,
    UnusedVolumes = 5,
}
