using Microsoft.VisualBasic.FileIO;
using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Security;

namespace SpaceSnoop.Core;

public readonly record struct ZipStats(int Count, long Bytes);

public readonly record struct ZipEntryStamp(long Length, DateTime Modified, uint Crc32);

public readonly record struct VerifyResult(bool Ok, string Detail);

public readonly record struct ArchiveContent(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> EmptyDirectories,
    IReadOnlyList<string> Unreadable,
    long Bytes,
    IReadOnlyList<string>? ReparsePoints = null,
    IReadOnlyList<string>? AlternateStreams = null);

internal sealed class ArchiveIndex
{
    private readonly Dictionary<string, ZipEntryStamp> _entries;
    private readonly HashSet<string> _directories;

    internal IEqualityComparer<string> Names => _entries.Comparer;

    private ArchiveIndex(StringComparer names)
    {
        _entries = new(names);
        _directories = new(names);
    }

    internal static ArchiveIndex Read(string zipPath, StringComparer names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var index = new ArchiveIndex(names);

        using var zip = ZipFile.OpenRead(zipPath);

        foreach (var entry in zip.Entries)
        {
            index._entries[entry.FullName] = new(entry.Length, entry.LastWriteTime.DateTime, entry.Crc32);

            for (var slash = entry.FullName.IndexOf('/'); slash >= 0; slash = entry.FullName.IndexOf('/', slash + 1))
            {
                index._directories.Add(entry.FullName[..(slash + 1)]);
            }
        }

        return index;
    }

    internal bool HasEntry(string name)
    {
        return _entries.ContainsKey(name);
    }

    internal bool HasDirectory(string name)
    {
        return _directories.Contains(name);
    }

    internal ZipEntryStamp Stamp(string name)
    {
        return _entries[name];
    }
}

public sealed class ArchiveService
{
    private const int BufferSize = 80 * 1024;
    private const int ProgressChunkStep = 1024 * 1024;
    private const int ZipMinYear = 1980;
    private const int ZipMaxYear = 2107;
    private const int FindStreamInfoStandard = 0;
    private const int ErrorInvalidFunction = 1;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorHandleEof = 38;
    private const int ErrorNotSupported = 50;
    private const int ErrorInvalidParameter = 87;
    private const string DefaultDataStream = "::$DATA";
    private const string DataStreamSuffix = ":$DATA";
    private const string CaseCollision = "в архиве не отличить по регистру путь";

    private static readonly IntPtr InvalidHandle = new(-1);

    public ArchiveContent Collect(string root, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var files = new List<string>();
        var emptyDirectories = new List<string>();
        var unreadable = new List<string>();
        var reparsePoints = new List<string>();
        var alternateStreams = new List<string>();
        long bytes = 0;

        var stack = new Stack<DirectoryInfo>();
        var rootDirectory = new DirectoryInfo(root);

        try
        {
            if (IsReparsePoint(rootDirectory) && !ReparsePoint.IsMountedVolume(rootDirectory.FullName))
            {
                reparsePoints.Add(rootDirectory.FullName);
                return new(files, emptyDirectories, unreadable, bytes, reparsePoints, alternateStreams);
            }

            stack.Push(rootDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            Debug.WriteLine(exception);
            unreadable.Add(rootDirectory.FullName);
            return new(files, emptyDirectories, unreadable, bytes, reparsePoints, alternateStreams);
        }

        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();

            var directory = stack.Pop();

            try
            {
                ReadDirectory(
                    directory,
                    root,
                    stack,
                    files,
                    emptyDirectories,
                    reparsePoints,
                    alternateStreams,
                    ref bytes,
                    token);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                Debug.WriteLine(exception);
                unreadable.Add(directory.FullName);
            }
        }

        return new(files, emptyDirectories, unreadable, bytes, reparsePoints, alternateStreams);
    }

    public ZipStats ZipFiles(
        string root,
        ArchiveContent content,
        string zipPath,
        CompressionLevel level,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        var prefix = Prefix(root);
        long bytes = 0;
        var written = 0;

        var target = CreateArchiveFile(zipPath);

        try
        {
            using (target)
            using (var zip = new ZipArchive(target, ZipArchiveMode.Create))
            {
                foreach (var file in content.Files)
                {
                    token.ThrowIfCancellationRequested();

                    if (!File.Exists(file))
                    {
                        continue;
                    }

                    var rel = Relative(prefix, file);

                    bytes += WriteEntry(zip, file, rel, level, token);
                    written++;
                    progress?.Report(new(written, rel));
                }

                foreach (var directory in content.EmptyDirectories)
                {
                    token.ThrowIfCancellationRequested();

                    if (!Directory.Exists(directory))
                    {
                        continue;
                    }

                    var rel = Relative(prefix, directory) + "/";

                    zip.CreateEntry(rel, level);
                    written++;
                    progress?.Report(new(written, rel));
                }

                token.ThrowIfCancellationRequested();
            }
        }
        catch
        {
            SafeDelete(zipPath);
            throw;
        }

        return new(written, bytes);
    }

    public VerifyResult VerifyCoverage(string root, string zipPath, CancellationToken token)
    {
        return VerifyCoverage(root, zipPath, null, null, token);
    }

    public VerifyResult VerifyCoverage(
        string root,
        string zipPath,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        return VerifyCoverage(root, zipPath, progress, null, token);
    }

    public VerifyResult VerifyCoverage(
        string root,
        string zipPath,
        IProgress<OperationProgress>? progress,
        Action<int, long>? onTotals,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        // TODO: чувствительность снимается с корня упаковки и наследуется поддеревом, поэтому у
        // подкаталога с включённой per-directory чувствительностью (fsutil file setCaseSensitiveInfo)
        // имена, различающиеся только регистром, дают отказ в покрытии вместо разбора – снимать политику
        // на каждый каталог, когда в упаковку пойдут деревья WSL.
        var index = ArchiveIndex.Read(zipPath, PathCase.ComparerFor(root));
        var content = Collect(root, token);

        if (CheckCoverage(root, content, index) is { Ok: false } coverage)
        {
            return coverage;
        }

        onTotals?.Invoke(content.Files.Count, content.Bytes);

        if (CheckContent(root, content, index, progress, token) is { Ok: false } rewritten)
        {
            return rewritten;
        }

        return CheckCoverage(root, Collect(root, token), index);
    }

    public VerifyResult VerifyZip(
        string zipPath,
        ZipStats expected,
        bool checkContent = false,
        IProgress<OperationProgress>? progress = null,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        using var zip = ZipFile.OpenRead(zipPath);

        if (zip.Entries.Count != expected.Count)
        {
            return new(false, $"файлов в архиве {zip.Entries.Count}, ожидалось {expected.Count}");
        }

        long bytes = 0;

        foreach (var entry in zip.Entries)
        {
            bytes += entry.Length;
        }

        if (bytes != expected.Bytes)
        {
            return new(false, $"объём {SizeFormatter.Format(bytes)} ≠ {SizeFormatter.Format(expected.Bytes)}");
        }

        return checkContent ? VerifyContent(zip, progress, token) : new(true, string.Empty);
    }

    public void DeleteDirectoryToRecycleBin(string path, bool showUi)
    {
        if (showUi)
        {
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return;
        }

        RecycleBin.DeleteSilent(path);
    }

    public void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(exception);
        }
    }

    internal VerifyResult CheckCoverage(string root, ArchiveContent content, ArchiveIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);

        if (content.Unreadable.Count > 0)
        {
            return new(false, Detail("каталог не читается", content.Unreadable[0], content.Unreadable.Count));
        }

        if (content.ReparsePoints is { Count: > 0 } reparsePoints)
        {
            return new(false, Detail("в архив не попала точка повторного разбора", reparsePoints[0], reparsePoints.Count));
        }

        if (content.AlternateStreams is { Count: > 0 } alternateStreams)
        {
            return new(false, Detail("в архив не попал альтернативный поток NTFS", alternateStreams[0], alternateStreams.Count));
        }

        var prefix = Prefix(root);
        var keys = new HashSet<string>(index.Names);

        if (Missing(content.Files, x => keys.Add(Relative(prefix, x)), CaseCollision) is { } ambiguousFile)
        {
            return new(false, ambiguousFile);
        }

        if (Missing(content.EmptyDirectories, x => keys.Add(Relative(prefix, x) + "/"), CaseCollision) is { } ambiguousDirectory)
        {
            return new(false, ambiguousDirectory);
        }

        if (Missing(content.Files, x => index.HasEntry(Relative(prefix, x)), "в архив не попал файл") is { } file)
        {
            return new(false, file);
        }

        if (Missing(content.Files, x => Unchanged(index.Stamp(Relative(prefix, x)), x), "после упаковки изменился файл") is { } changed)
        {
            return new(false, changed);
        }

        if (Missing(content.EmptyDirectories, x => index.HasDirectory(Relative(prefix, x) + "/"), "в архив не попал пустой каталог") is { } empty)
        {
            return new(false, empty);
        }

        return new(true, string.Empty);
    }

    internal VerifyResult CheckContent(string root, ArchiveContent content, ArchiveIndex index, CancellationToken token)
    {
        return CheckContent(root, content, index, null, token);
    }

    // TODO: содержимое сверяется по CRC-32 из центрального каталога – 32 бита ловят случайную правку, но не
    // подобранную подмену; сверять побайтно с распакованной записью, когда упаковка пойдёт по каталогам, куда
    // пишут недоверенные процессы.
    internal VerifyResult CheckContent(
        string root,
        ArchiveContent content,
        ArchiveIndex index,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(index);

        var prefix = Prefix(root);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var completed = 0;
        long totalRead = 0;
        long reportedBytes = 0;

        try
        {
            foreach (var file in content.Files)
            {
                token.ThrowIfCancellationRequested();

                var rel = Relative(prefix, file);

                if (ContentMismatch(index.Stamp(rel), file, buffer, completed, rel, ref totalRead, ref reportedBytes, progress, token) is { } mismatch)
                {
                    return new(false, mismatch);
                }

                completed++;
                reportedBytes = totalRead;
                progress?.Report(new(completed, rel, totalRead));
            }

            token.ThrowIfCancellationRequested();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return new(true, string.Empty);
    }

    private static void ReadDirectory(
        DirectoryInfo directory,
        string root,
        Stack<DirectoryInfo> stack,
        List<string> files,
        List<string> emptyDirectories,
        List<string> reparsePoints,
        List<string> alternateStreams,
        ref long bytes,
        CancellationToken token)
    {
        CollectAlternateStreams(directory.FullName, true, alternateStreams);

        var found = 0;
        var skipped = 0;

        foreach (var file in directory.GetFiles())
        {
            token.ThrowIfCancellationRequested();

            if (IsReparsePoint(file))
            {
                reparsePoints.Add(file.FullName);
                skipped++;
                continue;
            }

            if (!CollectAlternateStreams(file.FullName, false, alternateStreams))
            {
                continue;
            }

            files.Add(file.FullName);
            bytes += file.Length;
            found++;
        }

        var traversable = 0;

        foreach (var sub in directory.GetDirectories())
        {
            token.ThrowIfCancellationRequested();

            if (IsReparsePoint(sub))
            {
                reparsePoints.Add(sub.FullName);
                skipped++;
                continue;
            }

            stack.Push(sub);
            traversable++;
        }

        if (found == 0 && traversable == 0 && skipped == 0 && !IsSameDirectory(directory.FullName, root))
        {
            emptyDirectories.Add(directory.FullName);
        }
    }

    private static bool CollectAlternateStreams(string path, bool isDirectory, List<string> alternateStreams)
    {
        var handle = FindFirstStream(ReparsePoint.Extended(path), FindStreamInfoStandard, out var data, 0);

        if (handle == InvalidHandle)
        {
            var error = Marshal.GetLastWin32Error();

            if (error is ErrorHandleEof or ErrorInvalidParameter or ErrorNotSupported or ErrorInvalidFunction)
            {
                return true;
            }

            if (!isDirectory && error is ErrorFileNotFound or ErrorPathNotFound)
            {
                return false;
            }

            throw new IOException(new Win32Exception(error).Message, HResultFromWin32(error));
        }

        try
        {
            do
            {
                if (!string.Equals(data.cStreamName, DefaultDataStream, StringComparison.OrdinalIgnoreCase))
                {
                    alternateStreams.Add(FormatStreamPath(path, data.cStreamName));
                }
            } while (FindNextStream(handle, out data));

            var error = Marshal.GetLastWin32Error();

            if (error != ErrorHandleEof)
            {
                throw new IOException(new Win32Exception(error).Message, HResultFromWin32(error));
            }
        }
        finally
        {
            FindClose(handle);
        }

        return true;
    }

    private static int HResultFromWin32(int error)
    {
        return error <= 0 ? error : unchecked((int)((uint)(error & 0x0000_FFFF) | 0x8007_0000u));
    }

    private static string FormatStreamPath(string path, string streamName)
    {
        return streamName.EndsWith(DataStreamSuffix, StringComparison.OrdinalIgnoreCase) && streamName.Length > DataStreamSuffix.Length
            ? path + streamName[..^DataStreamSuffix.Length]
            : path + streamName;
    }

    private static FileStream CreateArchiveFile(string zipPath)
    {
        try
        {
            return new(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && (File.Exists(zipPath) || Directory.Exists(zipPath)))
        {
            throw new IOException($"Имя «{Path.GetFileName(zipPath)}» уже занято – архив не создан, существующий файл не тронут.", exception);
        }
    }

    private static string Prefix(string root)
    {
        return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string Relative(string prefix, string path)
    {
        return (path.Length > (prefix.Length + 1) ? path[(prefix.Length + 1)..] : Path.GetFileName(path))
            .Replace('\\', '/');
    }

    private static bool IsReparsePoint(FileSystemInfo info)
    {
        return (info.Attributes & FileAttributes.ReparsePoint) != 0;
    }

    private static bool Unchanged(ZipEntryStamp stamp, string path)
    {
        try
        {
            var file = new FileInfo(path);

            return file.Length == stamp.Length
                && (file.LastWriteTime - stamp.Modified).Duration() <= DirectoryComparer.FatTimestampTolerance;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            Debug.WriteLine(exception);
            return false;
        }
    }

    private static bool IsSameDirectory(string left, string right)
    {
        return string.Equals(Prefix(left), Prefix(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string? Missing(IReadOnlyList<string> paths, Func<string, bool> covered, string reason)
    {
        string? first = null;
        var count = 0;

        foreach (var path in paths)
        {
            if (covered(path))
            {
                continue;
            }

            first ??= path;
            count++;
        }

        return first is null ? null : Detail(reason, first, count);
    }

    private static string Detail(string reason, string first, int count)
    {
        return count > 1
            ? $"{reason} «{first}», всего таких путей {count}"
            : $"{reason} «{first}»";
    }

    private static long WriteEntry(ZipArchive zip, string path, string entryName, CompressionLevel level, CancellationToken token)
    {
        var entry = zip.CreateEntry(entryName, level);
        var lastWrite = File.GetLastWriteTime(path);

        entry.LastWriteTime = lastWrite.Year switch
        {
            < ZipMinYear => new(ZipMinYear, 1, 1, 0, 0, 0, DateTimeKind.Local),
            > ZipMaxYear => new(ZipMaxYear, 12, 31, 23, 59, 58, DateTimeKind.Local),
            _ => lastWrite,
        };

        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var target = entry.Open();

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long copied = 0;

        try
        {
            int read;

            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                target.Write(buffer, 0, read);
                copied += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return copied;
    }

    private static VerifyResult VerifyContent(ZipArchive zip, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var done = 0;

        try
        {
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();

                if (VerifyEntry(entry, buffer, token) is { } failure)
                {
                    return new(false, failure);
                }

                done++;
                progress?.Report(new(done, entry.FullName));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return new(true, string.Empty);
    }

    private static string? VerifyEntry(ZipArchiveEntry entry, byte[] buffer, CancellationToken token)
    {
        var crc = new Crc32();
        long read = 0;

        try
        {
            using var stream = entry.Open();
            int chunk;

            while ((chunk = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                crc.Append(buffer.AsSpan(0, chunk));
                read += chunk;
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return $"«{entry.FullName}» не читается: {exception.Message}";
        }

        if (read != entry.Length)
        {
            return $"«{entry.FullName}»: прочитано {SizeFormatter.Format(read)}, объявлено {SizeFormatter.Format(entry.Length)}";
        }

        return crc.GetCurrentHashAsUInt32() == entry.Crc32
            ? null
            : $"«{entry.FullName}»: контрольная сумма не сошлась";
    }

    private static string? ContentMismatch(
        ZipEntryStamp stamp,
        string path,
        byte[] buffer,
        int completed,
        string relativePath,
        ref long totalRead,
        ref long reportedBytes,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        var crc = new Crc32();
        long read = 0;

        try
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            int chunk;

            while ((chunk = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                crc.Append(buffer.AsSpan(0, chunk));
                read += chunk;
                totalRead += chunk;

                if (progress is not null && (totalRead - reportedBytes) >= ProgressChunkStep)
                {
                    reportedBytes = totalRead;
                    progress.Report(new(completed, relativePath, totalRead));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return $"файл не прочитан для сверки с архивом «{path}»: {exception.Message}";
        }

        return read == stamp.Length && crc.GetCurrentHashAsUInt32() == stamp.Crc32
            ? null
            : $"после упаковки изменилось содержимое файла «{path}»";
    }

    [DllImport("kernel32.dll", EntryPoint = "FindFirstStreamW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstStream(
        string lpFileName,
        int infoLevel,
        out Win32FindStreamData lpFindStreamData,
        uint dwFlags);

    [DllImport("kernel32.dll", EntryPoint = "FindNextStreamW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextStream(
        IntPtr hFindStream,
        out Win32FindStreamData lpFindStreamData);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr hFindFile);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Win32FindStreamData
    {
        public long StreamSize;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)]
        public string cStreamName;
    }
}
