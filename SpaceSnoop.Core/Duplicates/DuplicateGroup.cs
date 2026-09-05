namespace SpaceSnoop.Core.Duplicates;

public sealed record DuplicateGroup(long Size, IReadOnlyList<DuplicateMember> Members, int DistinctFiles, int OmittedMembers = 0)
{
    public long ReclaimableBytes => Math.Max(0, DistinctFiles - 1) * Size;
}
