using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace SpaceSnoop.Core.Mft;

internal delegate bool MftRecordSliceFetcher(int recordNumber, out ReadOnlySpan<byte> outputBuffer);

internal sealed class MftUsnNativeSource : IMftUsnSource
{
    private const uint FsctlQueryUsnJournal = 0x000900F4u;
    private const uint FsctlReadUsnJournal = 0x000900BBu;
    private const uint FsctlGetNtfsFileRecord = 0x00090068u;

    private const uint GenericRead = 0x80000000u;
    private const uint FileReadAttributes = 0x00000080u;
    private const uint FileShareAll = 0x00000007u;
    private const uint OpenExisting = 3u;
    private const uint FileFlagBackupSemantics = 0x02000000u;
    private const int FileStandardInfoClass = 1;
    private const int FileIdType = 0;

    private const int ErrorInvalidFunction = 1;
    private const int ErrorAccessDenied = 5;
    private const int ErrorNotSupported = 50;
    private const int ErrorInvalidParameter = 87;
    private const int ErrorJournalDeleteInProgress = 1178;
    private const int ErrorJournalNotActive = 1179;
    private const int ErrorJournalEntryDeleted = 1181;

    private const int UsnBufferBytes = 64 * 1024;
    private const int FileRecordOutputHeaderBytes = 12;
    private const int MaxNtfsRecordBytes = 4096;
    private const int MftRecordMinHeaderBytes = 48;
    private const int ResidentHeaderBytes = 0x16;
    private const int NonResidentHeaderBytes = 0x38;
    private const int FileNameHeaderBytes = 0x42;
    private const int AttributeListEntryBytes = 0x1A;
    private const int UsnRecordV2MinBytes = 60;

    private readonly SafeFileHandle _volumeHandle;
    private readonly SafeFileHandle _rootHandle;
    private readonly byte[] _usnBuffer = new byte[UsnBufferBytes];
    private readonly byte[] _recordBuffer = new byte[FileRecordOutputHeaderBytes + MaxNtfsRecordBytes];
    private readonly byte[] _extensionBuffer = new byte[FileRecordOutputHeaderBytes + MaxNtfsRecordBytes];
    private readonly ExtensionFetchState _extensionState = new();
    private readonly MftRecordSliceFetcher _extensionFetcher;
    private bool _disposed;

    private MftUsnNativeSource(SafeFileHandle volumeHandle, SafeFileHandle rootHandle)
    {
        _volumeHandle = volumeHandle;
        _rootHandle = rootHandle;
        _extensionFetcher = TryFetchExtensionBuffer;
    }

    public static bool TryOpen(char letter, out MftUsnNativeSource? source, out MftUsnErrorKind error, out int win32Error)
    {
        source = null;

        if (!OperatingSystem.IsWindows())
        {
            error = MftUsnErrorKind.NotSupported;
            win32Error = ErrorNotSupported;
            return false;
        }

        var upper = char.ToUpperInvariant(letter);

        if (upper is < 'A' or > 'Z')
        {
            error = MftUsnErrorKind.InvalidParameter;
            win32Error = ErrorInvalidParameter;
            return false;
        }

        var volumeHandle = CreateFileW($@"\\.\{upper}:", GenericRead, FileShareAll, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        if (volumeHandle.IsInvalid)
        {
            win32Error = Marshal.GetLastWin32Error();
            error = ClassifyWin32Error(win32Error);
            volumeHandle.Dispose();
            return false;
        }

        var rootHandle = CreateFileW(
            $@"{upper}:\",
            FileReadAttributes,
            FileShareAll,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);

        if (rootHandle.IsInvalid)
        {
            win32Error = Marshal.GetLastWin32Error();
            error = ClassifyWin32Error(win32Error);
            rootHandle.Dispose();
            volumeHandle.Dispose();
            return false;
        }

        source = new MftUsnNativeSource(volumeHandle, rootHandle);
        error = MftUsnErrorKind.None;
        win32Error = 0;
        return true;
    }

    public bool TryQueryJournal(out MftUsnJournalInfo info, out MftUsnErrorKind error, out int win32Error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!DeviceIoControlQueryJournal(
                _volumeHandle,
                FsctlQueryUsnJournal,
                IntPtr.Zero,
                0,
                out var data,
                Marshal.SizeOf<UsnJournalDataV0>(),
                out _,
                IntPtr.Zero))
        {
            win32Error = Marshal.GetLastWin32Error();
            error = ClassifyWin32Error(win32Error);
            info = default;
            return false;
        }

        if (!GetFileInformationByHandle(_rootHandle, out var fileInfo))
        {
            win32Error = Marshal.GetLastWin32Error();
            error = ClassifyWin32Error(win32Error);
            info = default;
            return false;
        }

        info = new MftUsnJournalInfo(
            data.UsnJournalID,
            data.FirstUsn,
            data.NextUsn,
            data.LowestValidUsn,
            fileInfo.VolumeSerialNumber);
        error = MftUsnErrorKind.None;
        win32Error = 0;
        return true;
    }

    public bool TryReadDelta(
        ulong journalId,
        long startUsn,
        int maxEvents,
        List<MftUsnEvent> destination,
        out long nextUsn,
        out MftUsnErrorKind error,
        out int win32Error,
        CancellationToken cancel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        nextUsn = startUsn;

        var cursorUsn = startUsn;
        var input = new ReadUsnJournalDataV0
        {
            StartUsn = startUsn,
            ReasonMask = 0xFFFFFFFFu,
            ReturnOnlyOnClose = 0u,
            Timeout = 0UL,
            BytesToWaitFor = 0UL,
            UsnJournalID = journalId,
        };

        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            input.StartUsn = cursorUsn;

            if (!DeviceIoControlReadUsn(
                    _volumeHandle,
                    FsctlReadUsnJournal,
                    ref input,
                    Marshal.SizeOf<ReadUsnJournalDataV0>(),
                    _usnBuffer,
                    _usnBuffer.Length,
                    out var returned,
                    IntPtr.Zero))
            {
                win32Error = Marshal.GetLastWin32Error();
                error = ClassifyWin32Error(win32Error);
                nextUsn = startUsn;
                return false;
            }

            if (returned < 8 || returned > _usnBuffer.Length)
            {
                error = MftUsnErrorKind.IoError;
                win32Error = 0;
                nextUsn = startUsn;
                return false;
            }

            if (!ParseUsnJournalBuffer(_usnBuffer.AsSpan(0, returned), destination, out var batchNextUsn, out var parseError))
            {
                error = parseError;
                win32Error = 0;
                nextUsn = startUsn;
                return false;
            }

            if (maxEvents > 0 && destination.Count > maxEvents)
            {
                error = MftUsnErrorKind.DeltaLimitExceeded;
                win32Error = 0;
                nextUsn = startUsn;
                return false;
            }

            if (returned == 8 || batchNextUsn == cursorUsn)
            {
                cursorUsn = batchNextUsn;
                break;
            }

            cursorUsn = batchNextUsn;
        }

        nextUsn = cursorUsn;
        error = MftUsnErrorKind.None;
        win32Error = 0;
        return true;
    }

    public bool TryFetchFileRecord(int recordNumber, out MftUsnRecord record, out MftUsnErrorKind error, out int win32Error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (recordNumber < 0)
        {
            record = default;
            error = MftUsnErrorKind.None;
            win32Error = 0;
            return true;
        }

        var input = (long)recordNumber;

        if (!DeviceIoControlFileRecord(
                _volumeHandle,
                FsctlGetNtfsFileRecord,
                ref input,
                sizeof(long),
                _recordBuffer,
                _recordBuffer.Length,
                out var returned,
                IntPtr.Zero))
        {
            win32Error = Marshal.GetLastWin32Error();
            error = ClassifyWin32Error(win32Error);
            record = default;
            return false;
        }

        if (returned <= 0 || returned > _recordBuffer.Length)
        {
            win32Error = 0;
            error = MftUsnErrorKind.IoError;
            record = default;
            return false;
        }

        _extensionState.Reset();
        record = ParseFileRecordBuffer(_recordBuffer.AsSpan(0, returned), recordNumber, _extensionFetcher);

        if (_extensionState.Error != MftUsnErrorKind.None)
        {
            error = _extensionState.Error;
            win32Error = _extensionState.Win32Error;
            record = default;
            return false;
        }

        if (record.IsIncomplete)
        {
            error = MftUsnErrorKind.IoError;
            win32Error = 0;
            record = default;
            return false;
        }

        error = MftUsnErrorKind.None;
        win32Error = 0;
        return true;
    }

    public bool TryQueryLiveFileSize(ulong frn, out long size)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        size = 0;

        var descriptor = new FileIdDescriptor
        {
            Size = (uint)Marshal.SizeOf<FileIdDescriptor>(),
            Type = FileIdType,
            FileId = frn,
            Padding = 0UL,
        };

        using var fileHandle = OpenFileById(
            _rootHandle,
            in descriptor,
            FileReadAttributes,
            FileShareAll,
            IntPtr.Zero,
            FileFlagBackupSemantics);

        if (fileHandle.IsInvalid)
        {
            return false;
        }

        if (!GetFileInformationByHandleEx(
                fileHandle,
                FileStandardInfoClass,
                out var info,
                Marshal.SizeOf<FileStandardInfo>()))
        {
            return false;
        }

        if (info.Directory != 0 || info.EndOfFile < 0)
        {
            return false;
        }

        size = info.EndOfFile;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _rootHandle.Dispose();
        _volumeHandle.Dispose();
    }

    internal static MftUsnErrorKind ClassifyWin32Error(int win32Error)
    {
        return win32Error switch
        {
            0 => MftUsnErrorKind.None,
            ErrorJournalEntryDeleted => MftUsnErrorKind.JournalEntryDeleted,
            ErrorInvalidParameter => MftUsnErrorKind.InvalidParameter,
            ErrorJournalNotActive or ErrorJournalDeleteInProgress => MftUsnErrorKind.JournalNotActive,
            ErrorAccessDenied => MftUsnErrorKind.AccessDenied,
            ErrorInvalidFunction or ErrorNotSupported => MftUsnErrorKind.NotSupported,
            _ => MftUsnErrorKind.IoError,
        };
    }

    internal static bool ParseUsnJournalBuffer(
        ReadOnlySpan<byte> buffer,
        List<MftUsnEvent> destination,
        out long nextUsn)
    {
        return ParseUsnJournalBuffer(buffer, destination, out nextUsn, out _);
    }

    internal static bool ParseUsnJournalBuffer(
        ReadOnlySpan<byte> buffer,
        List<MftUsnEvent> destination,
        out long nextUsn,
        out MftUsnErrorKind error)
    {
        nextUsn = 0;

        if (buffer.Length < 8)
        {
            error = MftUsnErrorKind.IoError;
            return false;
        }

        var parsedNextUsn = BinaryPrimitives.ReadInt64LittleEndian(buffer);
        var initialCount = destination.Count;
        var offset = 8;

        while (offset <= buffer.Length - UsnRecordV2MinBytes)
        {
            var recLen = BinaryPrimitives.ReadUInt32LittleEndian(buffer[offset..]);

            if (recLen < UsnRecordV2MinBytes || recLen > (uint)(buffer.Length - offset))
            {
                destination.RemoveRange(initialCount, destination.Count - initialCount);
                error = MftUsnErrorKind.IoError;
                return false;
            }

            var majorVersion = BinaryPrimitives.ReadUInt16LittleEndian(buffer[(offset + 4)..]);

            if (majorVersion != 2)
            {
                destination.RemoveRange(initialCount, destination.Count - initialCount);
                error = MftUsnErrorKind.NotSupported;
                return false;
            }

            var frn = BinaryPrimitives.ReadUInt64LittleEndian(buffer[(offset + 8)..]);
            var reason = BinaryPrimitives.ReadUInt32LittleEndian(buffer[(offset + 40)..]);
            destination.Add(new MftUsnEvent(frn, reason));
            offset += (int)recLen;
        }

        if (offset != buffer.Length)
        {
            destination.RemoveRange(initialCount, destination.Count - initialCount);
            error = MftUsnErrorKind.IoError;
            return false;
        }

        nextUsn = parsedNextUsn;
        error = MftUsnErrorKind.None;
        return true;
    }

    internal static MftUsnRecord ParseFileRecordBuffer(ReadOnlySpan<byte> outputBuffer, int requestedRecordNumber)
    {
        return ParseFileRecordBuffer(outputBuffer, requestedRecordNumber, null);
    }

    internal static MftUsnRecord ParseFileRecordBuffer(
        ReadOnlySpan<byte> outputBuffer,
        int requestedRecordNumber,
        MftRecordSliceFetcher? fetchExtension)
    {
        if (requestedRecordNumber < 0)
        {
            return default;
        }

        if (!TryExtractRecordSlice(outputBuffer, requestedRecordNumber, out var rec, out var corrupt))
        {
            return corrupt ? new MftUsnRecord { IsIncomplete = true } : default;
        }

        var flags = BinaryPrimitives.ReadUInt16LittleEndian(rec[0x16..]);

        if ((flags & MftLayout.RecordInUse) == 0)
        {
            return default;
        }

        var baseRef = BinaryPrimitives.ReadUInt64LittleEndian(rec[0x20..]);

        if (baseRef != 0)
        {
            return default;
        }

        var attrOffset = (int)BinaryPrimitives.ReadUInt16LittleEndian(rec[0x14..]);

        if (attrOffset < MftLayout.UpdateSequenceMinOffset || attrOffset > rec.Length)
        {
            return new MftUsnRecord { IsIncomplete = true };
        }

        var seq = BinaryPrimitives.ReadUInt16LittleEndian(rec[MftLayout.RecordSequenceOffset..]);
        var isDir = (flags & MftLayout.RecordIsDirectory) != 0;

        var state = new ParsedRecordState(isDir);
        var attrListBytes = ReadOnlySpan<byte>.Empty;

        ParseAttributes(rec, attrOffset, ref state, ref attrListBytes);

        if (!attrListBytes.IsEmpty)
        {
            if (fetchExtension is null)
            {
                state.IsIncomplete = true;
            }
            else
            {
                ResolveAttributeListExtensions(
                    attrListBytes,
                    requestedRecordNumber,
                    seq,
                    fetchExtension,
                    ref state);
            }
        }

        return state.ToRecord(seq);
    }

    private bool TryFetchExtensionBuffer(int recordNumber, out ReadOnlySpan<byte> outputBuffer)
    {
        if (recordNumber < 0)
        {
            outputBuffer = default;
            return false;
        }

        var input = (long)recordNumber;

        if (!DeviceIoControlFileRecord(
                _volumeHandle,
                FsctlGetNtfsFileRecord,
                ref input,
                sizeof(long),
                _extensionBuffer,
                _extensionBuffer.Length,
                out var returned,
                IntPtr.Zero))
        {
            var win32 = Marshal.GetLastWin32Error();
            _extensionState.Set(ClassifyWin32Error(win32), win32);
            outputBuffer = default;
            return false;
        }

        if (returned <= 0 || returned > _extensionBuffer.Length)
        {
            _extensionState.Set(MftUsnErrorKind.IoError, 0);
            outputBuffer = default;
            return false;
        }

        outputBuffer = _extensionBuffer.AsSpan(0, returned);
        return true;
    }

    private static bool TryExtractRecordSlice(
        ReadOnlySpan<byte> outputBuffer,
        int expectedRecordNumber,
        out ReadOnlySpan<byte> record,
        out bool corrupt)
    {
        record = default;

        if (outputBuffer.Length < FileRecordOutputHeaderBytes + MftRecordMinHeaderBytes)
        {
            corrupt = true;
            return false;
        }

        var returnedFrn = BinaryPrimitives.ReadUInt64LittleEndian(outputBuffer);
        var returnedRec = (long)(returnedFrn & (ulong)MftLayout.ReferenceMask);

        if (returnedRec != expectedRecordNumber)
        {
            corrupt = false;
            return false;
        }

        var recLen = BinaryPrimitives.ReadUInt32LittleEndian(outputBuffer[8..]);

        if (recLen < MftRecordMinHeaderBytes || recLen > (uint)(outputBuffer.Length - FileRecordOutputHeaderBytes))
        {
            corrupt = true;
            return false;
        }

        var slice = outputBuffer.Slice(FileRecordOutputHeaderBytes, (int)recLen);

        if (!"FILE"u8.SequenceEqual(slice[..4]))
        {
            corrupt = true;
            return false;
        }

        record = slice;
        corrupt = false;
        return true;
    }

    private static void ParseAttributes(
        ReadOnlySpan<byte> rec,
        int attrOffset,
        ref ParsedRecordState state,
        ref ReadOnlySpan<byte> attrListBytes)
    {
        var pos = attrOffset;

        while (pos <= rec.Length - 8)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(rec[pos..]);

            if (type == MftLayout.AttributeEnd)
            {
                break;
            }

            var len = BinaryPrimitives.ReadUInt32LittleEndian(rec[(pos + 4)..]);

            if (len < ResidentHeaderBytes || len > (uint)(rec.Length - pos))
            {
                state.IsIncomplete = true;
                break;
            }

            var attr = rec.Slice(pos, (int)len);
            var nonResident = attr[0x08] != 0;
            var nameLen = attr[0x09];

            switch (type)
            {
                case MftLayout.AttributeStandardInformation when !nonResident && !state.HasStandardTimes:
                    if (TryGetResidentValue(attr, out var siValue) && siValue.Length >= 0x20)
                    {
                        state.CreationFileTime = BinaryPrimitives.ReadInt64LittleEndian(siValue);
                        state.LastAccessFileTime = BinaryPrimitives.ReadInt64LittleEndian(siValue[0x18..]);
                        state.HasStandardTimes = true;
                    }

                    break;

                case MftLayout.AttributeList:
                    if (nonResident || !attrListBytes.IsEmpty || !TryGetResidentValue(attr, out var listValue))
                    {
                        state.IsIncomplete = true;
                    }
                    else
                    {
                        attrListBytes = listValue;
                    }

                    break;

                case MftLayout.AttributeFileName:
                    if (nonResident || !TryGetResidentValue(attr, out var fnValue))
                    {
                        state.IsIncomplete = true;
                    }
                    else
                    {
                        ReadFileName(fnValue, ref state);
                    }

                    break;

                case MftLayout.AttributeData when nameLen == 0 && !state.IsDirectory && !state.SizeKnown:
                    ReadDataSize(attr, nonResident, ref state);
                    break;

                case MftLayout.AttributeReparsePoint:
                    ReadReparse(attr, nonResident, ref state);
                    break;
            }

            pos += (int)len;
        }
    }

    private static void ResolveAttributeListExtensions(
        ReadOnlySpan<byte> attrListBytes,
        int baseRecordNumber,
        ushort baseSequence,
        MftRecordSliceFetcher fetchExtension,
        ref ParsedRecordState state)
    {
        HashSet<int>? fetched = null;
        var pos = 0;

        while (pos <= attrListBytes.Length - AttributeListEntryBytes)
        {
            var entryType = BinaryPrimitives.ReadUInt32LittleEndian(attrListBytes[pos..]);
            var entryLen = BinaryPrimitives.ReadUInt16LittleEndian(attrListBytes[(pos + 4)..]);

            if (entryLen < AttributeListEntryBytes || entryLen > attrListBytes.Length - pos)
            {
                state.IsIncomplete = true;
                break;
            }

            var entryNameLen = attrListBytes[pos + 6];
            var startVcn = BinaryPrimitives.ReadInt64LittleEndian(attrListBytes[(pos + 8)..]);
            var segRef = BinaryPrimitives.ReadUInt64LittleEndian(attrListBytes[(pos + 16)..]);
            var targetRecLong = (long)(segRef & (ulong)MftLayout.ReferenceMask);
            var targetSeq = (ushort)(segRef >> MftLayout.ReferenceMaskBits);

            var relevant = entryType == MftLayout.AttributeFileName
                || (entryType == MftLayout.AttributeData && entryNameLen == 0 && startVcn == 0)
                || entryType == MftLayout.AttributeReparsePoint
                || entryType == MftLayout.AttributeStandardInformation;

            if (relevant && targetRecLong != baseRecordNumber)
            {
                if (targetRecLong is <= 0 or > int.MaxValue)
                {
                    state.IsIncomplete = true;
                    return;
                }

                var targetRec = (int)targetRecLong;
                fetched ??= [];

                if (fetched.Add(targetRec))
                {
                    if (!fetchExtension(targetRec, out var extOutBuf))
                    {
                        state.IsIncomplete = true;
                        return;
                    }

                    ParseExtensionOutput(extOutBuf, targetRec, targetSeq, baseRecordNumber, baseSequence, ref state);
                }
            }

            pos += entryLen;
        }

        if (pos != attrListBytes.Length)
        {
            state.IsIncomplete = true;
        }
    }

    private static void ParseExtensionOutput(
        ReadOnlySpan<byte> outputBuffer,
        int expectedExtRecord,
        ushort expectedExtSequence,
        int baseRecordNumber,
        ushort baseSequence,
        ref ParsedRecordState state)
    {
        if (!TryExtractRecordSlice(outputBuffer, expectedExtRecord, out var ext, out _))
        {
            state.IsIncomplete = true;
            return;
        }

        var flags = BinaryPrimitives.ReadUInt16LittleEndian(ext[0x16..]);

        if ((flags & MftLayout.RecordInUse) == 0)
        {
            state.IsIncomplete = true;
            return;
        }

        var extSeq = BinaryPrimitives.ReadUInt16LittleEndian(ext[MftLayout.RecordSequenceOffset..]);

        if (!MftLayout.SameGeneration(extSeq, expectedExtSequence))
        {
            state.IsIncomplete = true;
            return;
        }

        var extBaseRef = BinaryPrimitives.ReadUInt64LittleEndian(ext[0x20..]);

        if (extBaseRef == 0)
        {
            state.IsIncomplete = true;
            return;
        }

        var extBaseRec = (long)(extBaseRef & (ulong)MftLayout.ReferenceMask);
        var extBaseSeq = (ushort)(extBaseRef >> MftLayout.ReferenceMaskBits);

        if (extBaseRec != baseRecordNumber || !MftLayout.SameGeneration(baseSequence, extBaseSeq))
        {
            state.IsIncomplete = true;
            return;
        }

        var attrOffset = (int)BinaryPrimitives.ReadUInt16LittleEndian(ext[0x14..]);

        if (attrOffset < MftLayout.UpdateSequenceMinOffset || attrOffset > ext.Length)
        {
            state.IsIncomplete = true;
            return;
        }

        var unusedList = ReadOnlySpan<byte>.Empty;
        ParseAttributes(ext, attrOffset, ref state, ref unusedList);
    }

    private static bool TryGetResidentValue(ReadOnlySpan<byte> attr, out ReadOnlySpan<byte> value)
    {
        value = default;

        if (attr.Length < ResidentHeaderBytes)
        {
            return false;
        }

        var valLen = BinaryPrimitives.ReadUInt32LittleEndian(attr[0x10..]);
        var valOff = BinaryPrimitives.ReadUInt16LittleEndian(attr[0x14..]);

        if (valOff > (uint)attr.Length || valLen > (uint)attr.Length - valOff)
        {
            return false;
        }

        value = attr.Slice(valOff, (int)valLen);
        return true;
    }

    private static void ReadFileName(ReadOnlySpan<byte> fnValue, ref ParsedRecordState state)
    {
        if (fnValue.Length < FileNameHeaderBytes)
        {
            return;
        }

        var nameLen = fnValue[0x40];
        var ns = fnValue[0x41];
        var requiredBytes = FileNameHeaderBytes + (nameLen * 2);

        if (nameLen == 0 || fnValue.Length < requiredBytes)
        {
            return;
        }

        var parentRef = BinaryPrimitives.ReadUInt64LittleEndian(fnValue);
        var parentLong = (long)(parentRef & (ulong)MftLayout.ReferenceMask);

        if (parentLong > int.MaxValue)
        {
            return;
        }

        var parent = (int)parentLong;
        var parentSeq = (ushort)(parentRef >> MftLayout.ReferenceMaskBits);
        var name = Encoding.Unicode.GetString(fnValue.Slice(FileNameHeaderBytes, nameLen * 2));

        if (!state.HasStandardTimes && !state.HasFileNameTimes)
        {
            state.FallbackCreationFileTime = BinaryPrimitives.ReadInt64LittleEndian(fnValue[0x08..]);
            state.FallbackLastAccessFileTime = BinaryPrimitives.ReadInt64LittleEndian(fnValue[0x20..]);
            state.HasFileNameTimes = true;
        }

        state.AddName(new MftUsnNameEntry(parent, parentSeq, name, ns));
    }

    private static void ReadDataSize(ReadOnlySpan<byte> attr, bool nonResident, ref ParsedRecordState state)
    {
        long size;

        if (nonResident)
        {
            if (attr.Length < NonResidentHeaderBytes)
            {
                state.IsIncomplete = true;
                return;
            }

            if (BinaryPrimitives.ReadInt64LittleEndian(attr[0x10..]) != 0)
            {
                return;
            }

            size = BinaryPrimitives.ReadInt64LittleEndian(attr[0x30..]);
        }
        else
        {
            if (attr.Length < ResidentHeaderBytes)
            {
                state.IsIncomplete = true;
                return;
            }

            var valLen = BinaryPrimitives.ReadUInt32LittleEndian(attr[0x10..]);
            var valOff = BinaryPrimitives.ReadUInt16LittleEndian(attr[0x14..]);

            if (valOff > (uint)attr.Length || valLen > (uint)attr.Length - valOff)
            {
                state.IsIncomplete = true;
                return;
            }

            size = valLen;
        }

        if (size < 0)
        {
            state.IsIncomplete = true;
            return;
        }

        state.Size = size;
        state.SizeKnown = true;
    }

    private static void ReadReparse(ReadOnlySpan<byte> attr, bool nonResident, ref ParsedRecordState state)
    {
        if (!nonResident && TryGetResidentValue(attr, out var value) && value.Length >= 4)
        {
            var tag = BinaryPrimitives.ReadUInt32LittleEndian(value);
            state.IsReparseLink = (tag & MftLayout.ReparseNameSurrogate) != 0;
            state.ReparseUnknown = false;
            return;
        }

        if (!state.IsReparseLink)
        {
            state.ReparseUnknown = true;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle OpenFileById(
        SafeFileHandle hVolumeHint,
        in FileIdDescriptor lpFileId,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwFlagsAndAttributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        int fileInformationClass,
        out FileStandardInfo lpFileInformation,
        int dwBufferSize);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControlQueryJournal(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        int nInBufferSize,
        out UsnJournalDataV0 lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControlReadUsn(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref ReadUsnJournalDataV0 lpInBuffer,
        int nInBufferSize,
        [Out] byte[] lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControlFileRecord(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref long lpInBuffer,
        int nInBufferSize,
        [Out] byte[] lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    private sealed class ExtensionFetchState
    {
        public MftUsnErrorKind Error { get; private set; }

        public int Win32Error { get; private set; }

        public void Reset()
        {
            Error = MftUsnErrorKind.None;
            Win32Error = 0;
        }

        public void Set(MftUsnErrorKind error, int win32Error)
        {
            Error = error;
            Win32Error = win32Error;
        }
    }

    private struct ParsedRecordState(bool isDirectory)
    {
        public readonly bool IsDirectory = isDirectory;
        public long Size;
        public bool SizeKnown;
        public bool IsReparseLink;
        public bool ReparseUnknown;
        public long CreationFileTime;
        public long LastAccessFileTime;
        public bool HasStandardTimes;
        public long FallbackCreationFileTime;
        public long FallbackLastAccessFileTime;
        public bool HasFileNameTimes;
        public bool IsIncomplete;

        private bool _hasPrimary;
        private MftUsnNameEntry _primary;
        private List<MftUsnNameEntry>? _alternates;
        private bool _hasDosFallback;
        private MftUsnNameEntry _dosFallback;

        public void AddName(MftUsnNameEntry candidate)
        {
            if (candidate.Namespace == MftLayout.NamespaceDos)
            {
                if (!_hasDosFallback)
                {
                    _dosFallback = candidate;
                    _hasDosFallback = true;
                }

                return;
            }

            if (!_hasPrimary)
            {
                _primary = candidate;
                _hasPrimary = true;
                return;
            }

            if (_primary.Parent == candidate.Parent
                && string.Equals(_primary.Name, candidate.Name, StringComparison.Ordinal))
            {
                if (NamespaceRank(candidate.Namespace) < NamespaceRank(_primary.Namespace))
                {
                    _primary = candidate;
                }

                return;
            }

            if (NamespaceRank(candidate.Namespace) < NamespaceRank(_primary.Namespace))
            {
                RemoveAlternateMatch(candidate.Parent, candidate.Name);
                _alternates ??= [];
                _alternates.Add(_primary);
                _primary = candidate;
                return;
            }

            AddOrUpgradeAlternate(candidate);
        }

        public readonly MftUsnRecord ToRecord(ushort sequence)
        {
            if (!_hasPrimary && !_hasDosFallback)
            {
                return IsIncomplete ? new MftUsnRecord { IsIncomplete = true } : default;
            }

            var chosen = _hasPrimary ? _primary : _dosFallback;
            var alternates = _hasPrimary ? _alternates : null;
            var totalNameCount = 1 + (alternates?.Count ?? 0);
            var creation = HasStandardTimes ? CreationFileTime : FallbackCreationFileTime;
            var lastAccess = HasStandardTimes ? LastAccessFileTime : FallbackLastAccessFileTime;
            var isLink = IsReparseLink || (IsDirectory && ReparseUnknown);

            return new MftUsnRecord
            {
                Exists = true,
                IsDirectory = IsDirectory,
                IsLink = isLink,
                Sequence = sequence,
                PrimaryParent = chosen.Parent,
                PrimaryParentSequence = chosen.ParentSequence,
                PrimaryName = chosen.Name,
                Alternates = alternates,
                Size = IsDirectory ? 0 : Size,
                SizeKnown = !IsDirectory && SizeKnown,
                CreationFileTime = creation,
                LastAccessFileTime = lastAccess,
                TotalNameCount = totalNameCount,
                IsIncomplete = IsIncomplete,
            };
        }

        private static int NamespaceRank(byte ns)
        {
            return ns switch
            {
                3 => 0,
                1 => 1,
                0 => 2,
                2 => 3,
                _ => 4,
            };
        }

        private void RemoveAlternateMatch(int parent, string name)
        {
            if (_alternates is null)
            {
                return;
            }

            for (var i = _alternates.Count - 1; i >= 0; i--)
            {
                if (_alternates[i].Parent == parent && string.Equals(_alternates[i].Name, name, StringComparison.Ordinal))
                {
                    _alternates.RemoveAt(i);
                }
            }
        }

        private void AddOrUpgradeAlternate(MftUsnNameEntry candidate)
        {
            if (_alternates is not null)
            {
                for (var i = 0; i < _alternates.Count; i++)
                {
                    var existing = _alternates[i];

                    if (existing.Parent == candidate.Parent
                        && string.Equals(existing.Name, candidate.Name, StringComparison.Ordinal))
                    {
                        if (NamespaceRank(candidate.Namespace) < NamespaceRank(existing.Namespace))
                        {
                            _alternates[i] = candidate;
                        }

                        return;
                    }
                }
            }

            _alternates ??= [];
            _alternates.Add(candidate);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UsnJournalDataV0
    {
        public ulong UsnJournalID;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReadUsnJournalDataV0
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalID;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdDescriptor
    {
        public uint Size;
        public int Type;
        public ulong FileId;
        public ulong Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileStandardInfo
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending;
        public byte Directory;
        public ushort Padding;
    }
}
