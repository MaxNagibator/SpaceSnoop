namespace SpaceSnoop.Core.Mft;

internal struct MftEntry
{
    public string? Name;
    public int Parent;
    public ushort ParentSequence;
    public ushort Sequence;
    public long Size;
    public DateTime CreationTime;
    public DateTime LastAccessTime;
    public uint ReparseTag;
    public int Names;
    public bool IsDirectory;
    public bool SizeKnown;
    public bool ReparseUnknown;
    public bool Present;

    public readonly bool Exists => Present && Name is not null;

    public readonly bool IsLink => (ReparseTag & MftLayout.ReparseNameSurrogate) != 0 || (ReparseUnknown && IsDirectory);
}

internal readonly record struct MftName(int Parent, ushort ParentSequence, string Name, ushort BaseSequence = 0);

internal readonly record struct MftPendingSize(long Size, ushort BaseSequence = 0);
