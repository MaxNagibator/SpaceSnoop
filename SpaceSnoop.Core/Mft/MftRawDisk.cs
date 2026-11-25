using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core.Mft;

internal readonly record struct MftBootInfo(int BytesPerSector, int BytesPerCluster, long MftCluster, int RecordSize);

internal readonly record struct MftDiskExtent(uint DiskNumber, long StartingOffset, long ExtentLength);

internal readonly record struct MftRawDiskAttempt(
    IMftVolume? Volume,
    long ExtentLength,
    string? FailureReason,
    long MftValidDataLength = 0)
{
    public static MftRawDiskAttempt Success(IMftVolume volume, long extentLength, long mftValidDataLength = 0)
    {
        return new(volume, extentLength, null, mftValidDataLength);
    }

    public static MftRawDiskAttempt Fail(string reason)
    {
        return new(null, 0, reason);
    }
}

internal sealed class MftRawDiskVolume : IMftVolume
{
    private readonly IMftVolume _disk;
    private readonly long _baseOffset;
    private readonly long _extentLength;

    internal MftRawDiskVolume(IMftVolume disk, long baseOffset, long extentLength)
    {
        if (baseOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseOffset));
        }

        if (extentLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(extentLength));
        }

        _disk = disk;
        _baseOffset = baseOffset;
        _extentLength = extentLength;
    }

    public static MftRawDiskVolume Open(uint diskNumber, long baseOffset, long extentLength)
    {
        var disk = MftFileVolume.Open($@"\\.\PhysicalDrive{diskNumber}", $"PhysicalDrive{diskNumber}");
        return new(disk, baseOffset, extentLength);
    }

    public void SetSectorSize(int bytesPerSector)
    {
        _disk.SetSectorSize(bytesPerSector);
    }

    public void ReadAt(long offset, Span<byte> buffer)
    {
        _disk.ReadAt(TranslateOffset(offset, buffer.Length), buffer);
    }

    public ValueTask ReadAtAsync(long offset, Memory<byte> buffer, CancellationToken cancel)
    {
        return _disk.ReadAtAsync(TranslateOffset(offset, buffer.Length), buffer, cancel);
    }

    private long TranslateOffset(long offset, int length)
    {
        if (offset < 0 || offset > _extentLength || length > _extentLength - offset)
        {
            throw new InvalidDataException(
                $"Чтение раздела через PhysicalDrive вышло за границы: смещение {offset:N0}, длина {length:N0}, размер раздела {_extentLength:N0}");
        }

        try
        {
            return checked(_baseOffset + offset);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException($"Переполнение смещения на PhysicalDrive: {_baseOffset:N0} + {offset:N0}", exception);
        }
    }

    public void Dispose()
    {
        _disk.Dispose();
    }
}

internal static class MftRawDiskGuard
{
    public const int BootSectorBytes = 512;
    public const int MinDiskExtentsBufferBytes = 32;
    public const int MinNtfsVolumeDataBytes = 72;

    public static bool ValidateExtents(
        ReadOnlySpan<byte> buffer,
        int bytesReturned,
        int bytesPerSector,
        out MftDiskExtent extent,
        out string? reason)
    {
        extent = default;

        if (bytesReturned < MinDiskExtentsBufferBytes || buffer.Length < MinDiskExtentsBufferBytes)
        {
            reason = "буфер экстентов тома короче ожидаемого";
            return false;
        }

        var extentCount = BinaryPrimitives.ReadUInt32LittleEndian(buffer);

        if (extentCount != 1)
        {
            reason = $"том занимает экстентов: {extentCount} вместо одного";
            return false;
        }

        var diskNumber = BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..]);
        var startingOffset = BinaryPrimitives.ReadInt64LittleEndian(buffer[16..]);
        var extentLength = BinaryPrimitives.ReadInt64LittleEndian(buffer[24..]);

        if (startingOffset < 0 || extentLength <= 0)
        {
            reason = $"недопустимые границы экстента диска ({startingOffset:N0}, {extentLength:N0})";
            return false;
        }

        if (bytesPerSector <= 0 || startingOffset % bytesPerSector != 0 || extentLength % bytesPerSector != 0)
        {
            reason = $"границы экстента не выровнены по сектору {bytesPerSector} Б";
            return false;
        }

        extent = new(diskNumber, startingOffset, extentLength);
        reason = null;
        return true;
    }

    public static bool ValidateNtfsVolumeData(
        ReadOnlySpan<byte> buffer,
        int bytesReturned,
        MftBootInfo boot,
        long extentLength,
        out string? reason)
    {
        return ValidateNtfsVolumeData(buffer, bytesReturned, boot, extentLength, out _, out reason);
    }

    public static bool ValidateNtfsVolumeData(
        ReadOnlySpan<byte> buffer,
        int bytesReturned,
        MftBootInfo boot,
        long extentLength,
        out long mftValidDataLength,
        out string? reason)
    {
        mftValidDataLength = 0;

        if (bytesReturned < MinNtfsVolumeDataBytes || buffer.Length < MinNtfsVolumeDataBytes)
        {
            reason = "ответ NTFS_VOLUME_DATA короче ожидаемого";
            return false;
        }

        var bytesPerSector = BinaryPrimitives.ReadUInt32LittleEndian(buffer[0x28..]);
        var bytesPerCluster = BinaryPrimitives.ReadUInt32LittleEndian(buffer[0x2C..]);
        var recordSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer[0x30..]);
        var validDataLength = BinaryPrimitives.ReadInt64LittleEndian(buffer[0x38..]);
        var mftStartLcn = BinaryPrimitives.ReadInt64LittleEndian(buffer[0x40..]);

        if (bytesPerSector != (uint)boot.BytesPerSector
            || bytesPerCluster != (uint)boot.BytesPerCluster
            || recordSize != (uint)boot.RecordSize
            || mftStartLcn != boot.MftCluster)
        {
            reason = "параметры NTFS не совпадают с загрузочным сектором тома";
            return false;
        }

        if (validDataLength <= 0)
        {
            reason = $"недопустимая длина $MFT ({validDataLength:N0})";
            return false;
        }

        if (boot.BytesPerCluster <= 0
            || extentLength <= 0
            || mftStartLcn <= 0
            || mftStartLcn > extentLength / boot.BytesPerCluster)
        {
            reason = "начало $MFT выходит за размер раздела";
            return false;
        }

        var selfOffset = mftStartLcn * boot.BytesPerCluster;

        if (selfOffset > extentLength || boot.RecordSize > extentLength - selfOffset)
        {
            reason = "собственная запись $MFT выходит за пределы раздела";
            return false;
        }

        mftValidDataLength = validDataLength;
        reason = null;
        return true;
    }

    public static bool ValidateBootSectorMatch(
        ReadOnlySpan<byte> rawBoot,
        ReadOnlySpan<byte> volumeBoot,
        out string? reason)
    {
        if (rawBoot.Length != BootSectorBytes
            || volumeBoot.Length != BootSectorBytes
            || !rawBoot.SequenceEqual(volumeBoot))
        {
            reason = "загрузочный сектор на физическом диске отличается от тома (вероятно, шифрование или смещённый раздел)";
            return false;
        }

        reason = null;
        return true;
    }

    public static bool ValidateRunsWithinPartition(
        IReadOnlyList<MftRun> runs,
        long partitionExtentLength,
        int bytesPerCluster,
        out string? reason)
    {
        if (bytesPerCluster <= 0 || partitionExtentLength <= 0)
        {
            reason = "недопустимые параметры раздела для проверки runlist";
            return false;
        }

        var maxClusters = partitionExtentLength / bytesPerCluster;

        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];

            if (run.Cluster < 0
                || run.Count <= 0
                || run.Cluster > maxClusters
                || run.Count > maxClusters - run.Cluster)
            {
                reason = $"экстент $MFT ({run.Cluster:N0}..+{run.Count:N0}) выходит за границы раздела";
                return false;
            }
        }

        reason = null;
        return true;
    }
}

internal static class MftRawDiskProbe
{
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000u;
    private const uint FsctlGetNtfsVolumeData = 0x00090064u;
    private const int ErrorMoreData = 234;

    // TODO: предел – чтение PhysicalDrive минует кэш NTFS последних секунд; триггер апгрейда – жалобы на потерю недавних записей при IsComplete (добавить сброс или VSS).
    public static MftRawDiskAttempt Open(char letter, ReadOnlySpan<byte> volumeBoot)
    {
        MftBootInfo boot;

        try
        {
            boot = MftReader.ParseBoot(volumeBoot, letter);
        }
        catch (InvalidDataException exception)
        {
            return MftRawDiskAttempt.Fail(exception.Message);
        }

        using var metadataHandle = MftFileVolume.OpenHandle($@"\\.\{letter}:", 0u);

        if (metadataHandle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            return MftRawDiskAttempt.Fail($"не удалось открыть том {letter}: для проверки экстентов (код {error})");
        }

        var extentBuffer = new byte[256];

        if (!DeviceIoControl(
                metadataHandle,
                IoctlVolumeGetVolumeDiskExtents,
                IntPtr.Zero,
                0,
                extentBuffer,
                (uint)extentBuffer.Length,
                out var extentBytes,
                IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            return MftRawDiskAttempt.Fail(
                error == ErrorMoreData
                    ? $"том {letter}: занимает больше одного экстента диска"
                    : $"не удалось получить экстенты тома {letter}: (код {error})");
        }

        if (!MftRawDiskGuard.ValidateExtents(extentBuffer, (int)extentBytes, boot.BytesPerSector, out var extent, out var extentReason))
        {
            return MftRawDiskAttempt.Fail(extentReason!);
        }

        var ntfsBuffer = new byte[96];

        if (!DeviceIoControl(
                metadataHandle,
                FsctlGetNtfsVolumeData,
                IntPtr.Zero,
                0,
                ntfsBuffer,
                (uint)ntfsBuffer.Length,
                out var ntfsBytes,
                IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            return MftRawDiskAttempt.Fail($"не удалось получить параметры NTFS тома {letter}: (код {error})");
        }

        if (!MftRawDiskGuard.ValidateNtfsVolumeData(ntfsBuffer, (int)ntfsBytes, boot, extent.ExtentLength, out var mftValidDataLength, out var ntfsReason))
        {
            return MftRawDiskAttempt.Fail(ntfsReason!);
        }

        MftRawDiskVolume? rawVolume = null;

        try
        {
            rawVolume = MftRawDiskVolume.Open(extent.DiskNumber, extent.StartingOffset, extent.ExtentLength);
            var rawBoot = new byte[MftRawDiskGuard.BootSectorBytes];
            rawVolume.ReadAt(0, rawBoot);

            if (!MftRawDiskGuard.ValidateBootSectorMatch(rawBoot, volumeBoot, out var bootReason))
            {
                rawVolume.Dispose();
                return MftRawDiskAttempt.Fail(bootReason!);
            }

            var opened = rawVolume;
            rawVolume = null;
            return MftRawDiskAttempt.Success(opened, extent.ExtentLength, mftValidDataLength);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or InvalidDataException)
        {
            rawVolume?.Dispose();
            return MftRawDiskAttempt.Fail(exception.Message);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        [Out] byte[] lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);
}
