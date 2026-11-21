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
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct MftNameRef(int offset, ushort chunk, ushort byteLength)
{
    public readonly int Offset = offset;
    public readonly ushort Chunk = chunk;
    public readonly ushort ByteLength = byteLength;

    public bool HasValue => ByteLength != 0;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MftEntry
{
    public long Size;
    public long CreationFileTime;
    public long LastAccessFileTime;
    public MftNameRef Name;
    public int Parent;
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

    public readonly bool Exists => Present && Name.HasValue;

    public readonly bool IsLink => IsReparseLink || (IsDirectory && ReparseUnknown);

    public void ClearName()
    {
        Name = default;
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

internal readonly record struct MftName(int Parent, ushort ParentSequence, MftNameRef Name, ushort BaseSequence = 0);

internal readonly record struct MftPendingSize(long Size, ushort BaseSequence = 0);
