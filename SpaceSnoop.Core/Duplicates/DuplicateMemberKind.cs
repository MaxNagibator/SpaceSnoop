namespace SpaceSnoop.Core.Duplicates;

public enum DuplicateMemberKind
{
    None = 0,
    Copy = 1,
    HardLink = 2,
    SymbolicLink = 3,
}
