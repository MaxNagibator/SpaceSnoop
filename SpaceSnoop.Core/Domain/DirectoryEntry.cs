using System.Runtime.InteropServices;

namespace SpaceSnoop.Core.Domain;

[StructLayout(LayoutKind.Sequential)]
internal struct DirectoryEntry
{
    public const byte DetachedFlag = 2;

    public long Size;
    public long TotalSize;
    public long CreationRaw;
    public long LastAccessRaw;
    public int NameId;
    public int ParentIndex;
    public int FirstChildDir;
    public int NextSiblingDir;
    public int SubDirCount;
    public int FileStart;
    public int FileCount;
    public int TotalFileCount;
    public int TotalDirCount;
    public SpaceState State;
    public byte Flags;
    public short Reserved;

    public readonly bool IsDeleted => State == SpaceState.Deleted;

    public readonly bool IsDetached => (Flags & DetachedFlag) != 0;

    public void SetDetached(bool detached)
    {
        if (detached)
        {
            Flags |= DetachedFlag;
        }
        else
        {
            Flags &= unchecked((byte)~DetachedFlag);
        }
    }

    public readonly DateTime GetCreationDate()
    {
        return TimestampEncoding.ToDateTime(CreationRaw, Flags);
    }

    public readonly DateTime GetLastAccessTime()
    {
        return TimestampEncoding.ToDateTime(LastAccessRaw, Flags);
    }
}
