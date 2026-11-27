using System.Runtime.InteropServices;

namespace SpaceSnoop.Core.Mft;

[Flags]
internal enum MftRecordState : byte
{
    None = 0,
    Present = 1,
    Directory = 2,
    SizeKnown = 4,
    ReparseUnknown = 8,
    ReparseLink = 16,
    HasName = 32,
}

[StructLayout(LayoutKind.Sequential)]
internal struct MftEntry
{
    public long Size;
    public long CreationFileTime;
    public long LastAccessFileTime;
    public int Parent;
    public int NameId;
    public ushort ParentSequence;
    public ushort Sequence;
    public ushort Names;
    public MftRecordState Flags;

    public bool Present
    {
        readonly get => (Flags & MftRecordState.Present) != 0;
        set => SetFlag(MftRecordState.Present, value);
    }

    public bool IsDirectory
    {
        readonly get => (Flags & MftRecordState.Directory) != 0;
        set => SetFlag(MftRecordState.Directory, value);
    }

    public bool SizeKnown
    {
        readonly get => (Flags & MftRecordState.SizeKnown) != 0;
        set => SetFlag(MftRecordState.SizeKnown, value);
    }

    public bool ReparseUnknown
    {
        readonly get => (Flags & MftRecordState.ReparseUnknown) != 0;
        set => SetFlag(MftRecordState.ReparseUnknown, value);
    }

    public bool IsReparseLink
    {
        readonly get => (Flags & MftRecordState.ReparseLink) != 0;
        set => SetFlag(MftRecordState.ReparseLink, value);
    }

    public bool HasName
    {
        readonly get => (Flags & MftRecordState.HasName) != 0;
        set => SetFlag(MftRecordState.HasName, value);
    }

    public readonly bool Exists => (Flags & (MftRecordState.Present | MftRecordState.HasName)) == (MftRecordState.Present | MftRecordState.HasName);

    public readonly bool IsLink => IsReparseLink || (IsDirectory && ReparseUnknown);

    public void SetName(int nameId)
    {
        NameId = nameId;
        HasName = true;
    }

    public void ClearName()
    {
        NameId = 0;
        HasName = false;
    }

    public void IncrementNames()
    {
        if (Names < ushort.MaxValue)
        {
            Names++;
        }
    }

    private void SetFlag(MftRecordState flag, bool enabled)
    {
        if (enabled)
        {
            Flags |= flag;
        }
        else
        {
            Flags &= ~flag;
        }
    }
}

internal readonly record struct MftName(int Parent, ushort ParentSequence, int NameId, ushort BaseSequence = 0);

internal readonly record struct MftPendingSize(long Size, ushort BaseSequence = 0);
