namespace SpaceSnoop.Core.Domain;

[Flags]
public enum SpaceState : byte
{
    None = 0,
    Added = 1 << 0,
    Modified = 1 << 1,
    Deleted = 1 << 2,
}
