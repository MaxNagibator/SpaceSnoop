namespace SpaceSnoop.Core.Domain;

[Flags]
public enum SpaceState : byte
{
    None = 0,
    Added = 1 << 0,
    Deleted = 1 << 1,
    Error = 1 << 2,
}
