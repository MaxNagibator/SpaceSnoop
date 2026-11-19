namespace SpaceSnoop.Core.Duplicates;

public sealed record DuplicateMember(FileSpace Space, string Path, DuplicateMemberKind Kind)
{
    public bool ReclaimsSpace => Kind == DuplicateMemberKind.Copy;
}
