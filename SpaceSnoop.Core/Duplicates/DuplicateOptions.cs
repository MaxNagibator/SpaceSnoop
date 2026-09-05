namespace SpaceSnoop.Core.Duplicates;

public readonly record struct DuplicateOptions(long MinSize, int MaxParallelism, int GroupLimit, int MemberLimit)
{
    public static DuplicateOptions Default { get; } = new(1, 1, 1000, 200);
}
