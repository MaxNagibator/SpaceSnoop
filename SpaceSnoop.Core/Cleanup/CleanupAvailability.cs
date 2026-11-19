namespace SpaceSnoop.Core.Cleanup;

public enum CleanupAvailability
{
    None = 0,
    Available = 1,
    Missing = 2,
    NeedsAdmin = 3,
    Unsupported = 4,
    Unsafe = 5,
    Failed = 6,
}
