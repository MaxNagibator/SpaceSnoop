namespace SpaceSnoop.Core.Mft;

internal enum MftUsnErrorKind
{
    None = 0,
    JournalEntryDeleted = 1,
    InvalidParameter = 2,
    JournalNotActive = 3,
    AccessDenied = 4,
    NotSupported = 5,
    IoError = 6,
    DeltaLimitExceeded = 7,
}

internal readonly record struct MftUsnJournalInfo(
    ulong JournalId,
    long FirstUsn,
    long NextUsn,
    long LowestValidUsn,
    uint VolumeSerialNumber);

internal readonly record struct MftUsnEvent(ulong Frn, uint Reason);

internal readonly record struct MftUsnNameEntry(int Parent, ushort ParentSequence, string Name, byte Namespace);

internal readonly struct MftUsnRecord
{
    private readonly string? _primaryName;

    public bool Exists { get; init; }

    public bool IsDirectory { get; init; }

    public bool IsLink { get; init; }

    public ushort Sequence { get; init; }

    public int PrimaryParent { get; init; }

    public ushort PrimaryParentSequence { get; init; }

    public string PrimaryName
    {
        get => _primaryName ?? string.Empty;
        init => _primaryName = value;
    }

    public List<MftUsnNameEntry>? Alternates { get; init; }

    public long Size { get; init; }

    public bool SizeKnown { get; init; }

    public long CreationFileTime { get; init; }

    public long LastAccessFileTime { get; init; }

    public int TotalNameCount { get; init; }
}

internal interface IMftUsnSource : IDisposable
{
    bool TryQueryJournal(out MftUsnJournalInfo info, out MftUsnErrorKind error, out int win32Error);

    bool TryReadDelta(
        ulong journalId,
        long startUsn,
        int maxEvents,
        List<MftUsnEvent> destination,
        out long nextUsn,
        out MftUsnErrorKind error,
        out int win32Error,
        CancellationToken cancel);

    bool TryFetchFileRecord(int recordNumber, out MftUsnRecord record, out MftUsnErrorKind error, out int win32Error);

    bool TryQueryLiveFileSize(ulong frn, out long size);
}
