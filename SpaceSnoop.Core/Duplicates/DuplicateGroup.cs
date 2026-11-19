namespace SpaceSnoop.Core.Duplicates;

public sealed record DuplicateGroup(
    long Size,
    IReadOnlyList<DuplicateMember> Members,
    int DistinctFiles,
    IReadOnlyList<DuplicateMember>? Hidden = null)
{
    public int OmittedMembers => Hidden?.Count ?? 0;

    public long ReclaimableBytes => Math.Max(0, DistinctFiles - 1) * Size;

    public DuplicateGroup? Without(Func<FileSpace, bool> gone)
    {
        var surviving = new List<DuplicateMember>(Members.Count + OmittedMembers);
        var copyGone = false;
        var removed = false;

        foreach (var member in Hidden is null ? Members : Members.Concat(Hidden))
        {
            if (member.Kind == DuplicateMemberKind.Copy)
            {
                copyGone = false;
            }

            if (gone(member.Space))
            {
                removed = true;
                copyGone |= member.Kind == DuplicateMemberKind.Copy;
                continue;
            }

            if (copyGone && member.Kind == DuplicateMemberKind.HardLink)
            {
                surviving.Add(
                    member with
                    {
                        Kind = DuplicateMemberKind.Copy,
                    });

                copyGone = false;
                continue;
            }

            surviving.Add(member);
        }

        if (!removed)
        {
            return this;
        }

        var distinct = surviving.Count(static x => x.Kind == DuplicateMemberKind.Copy);

        if (distinct < 2)
        {
            return null;
        }

        var shown = Members.Count;

        return surviving.Count > shown
            ? new(Size, surviving[..shown], distinct, surviving[shown..])
            : new(Size, surviving, distinct);
    }
}
