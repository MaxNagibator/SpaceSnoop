using Microsoft.VisualBasic.FileIO;
using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.IO.Hashing;
using System.Security;

namespace SpaceSnoop.Core;

public readonly record struct ZipStats(int Count, long Bytes);

public readonly record struct ZipEntryStamp(long Length, DateTime Modified);

public readonly record struct VerifyResult(bool Ok, string Detail);

public readonly record struct ArchiveContent(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> EmptyDirectories,
    IReadOnlyList<string> Unreadable,
    long Bytes);

internal sealed class ArchiveIndex
{
    private readonly Dictionary<string, ZipEntryStamp> _entries;
    private readonly HashSet<string> _directories;

    private ArchiveIndex(StringComparer names)
    {
        _entries = new(names);
        _directories = new(names);
    }

    internal IEqualityComparer<string> Names => _entries.Comparer;

    internal static ArchiveIndex Read(string zipPath, StringComparer names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var index = new ArchiveIndex(names);

        using var zip = ZipFile.OpenRead(zipPath);

        foreach (var entry in zip.Entries)
        {
            index._entries[entry.FullName] = new(entry.Length, entry.LastWriteTime.DateTime);

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
    private const int ZipMinYear = 1980;
    private const int ZipMaxYear = 2107;
    private const string CaseCollision = "в архиве не отличить по регистру путь";

    public ArchiveContent Collect(string root, CancellationToken token)
    {
        var files = new List<string>();
        var emptyDirectories = new List<string>();
        var unreadable = new List<string>();
        long bytes = 0;

        var stack = new Stack<DirectoryInfo>();
        stack.Push(new(root));

        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();

            var directory = stack.Pop();

            try
            {
                ReadDirectory(directory, root, stack, files, emptyDirectories, ref bytes);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                Debug.WriteLine(exception);
                unreadable.Add(directory.FullName);
            }
        }

        return new(files, emptyDirectories, unreadable, bytes);
    }

    private static void ReadDirectory(
        DirectoryInfo directory,
        string root,
        Stack<DirectoryInfo> stack,
        List<string> files,
        List<string> emptyDirectories,
        ref long bytes)
    {
        var found = 0;

        foreach (var file in directory.GetFiles())
        {
            if (IsReparsePoint(file))
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
            if (IsReparsePoint(sub))
            {
                continue;
            }

            stack.Push(sub);
            traversable++;
        }

        if (found == 0 && traversable == 0 && !IsSameDirectory(directory.FullName, root))
        {
            emptyDirectories.Add(directory.FullName);
        }
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

    public VerifyResult VerifyCoverage(string root, string zipPath, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        // TODO: чувствительность снимается с корня упаковки и наследуется поддеревом, поэтому у
        // подкаталога с включённой per-directory чувствительностью (fsutil file setCaseSensitiveInfo)
        // имена, различающиеся только регистром, дают отказ в покрытии вместо разбора – снимать политику
        // на каждый каталог, когда в упаковку пойдут деревья WSL.
        var index = ArchiveIndex.Read(zipPath, PathCase.ComparerFor(root));
        var content = Collect(root, token);

        return CheckCoverage(root, content, index);
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

    internal VerifyResult CheckCoverage(string root, ArchiveContent content, ArchiveIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);

        if (content.Unreadable.Count > 0)
        {
            return new(false, Detail("каталог не читается", content.Unreadable[0], content.Unreadable.Count));
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

    private static string Prefix(string root)
    {
        return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string Relative(string prefix, string path)
    {
        return (path.Length > prefix.Length + 1 ? path[(prefix.Length + 1)..] : Path.GetFileName(path))
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
            < ZipMinYear => new DateTime(ZipMinYear, 1, 1, 0, 0, 0, DateTimeKind.Local),
            > ZipMaxYear => new DateTime(ZipMaxYear, 12, 31, 23, 59, 58, DateTimeKind.Local),
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
}
