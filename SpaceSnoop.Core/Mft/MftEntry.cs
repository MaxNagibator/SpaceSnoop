namespace SpaceSnoop.Core.Mft;

internal struct MftEntry
{
    public string? Name;
    public int Parent;
    public long Size;
    public DateTime CreationTime;
    public DateTime LastAccessTime;
    public uint ReparseTag;
    public bool IsDirectory;
    public bool SizeKnown;

    public readonly bool Exists => Name is not null;

    public readonly bool IsLink => (ReparseTag & MftLayout.ReparseNameSurrogate) != 0;
}
