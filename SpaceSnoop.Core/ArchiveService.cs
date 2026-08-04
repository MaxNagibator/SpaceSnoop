using Microsoft.VisualBasic.FileIO;
using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.IO.Hashing;

namespace SpaceSnoop.Core;

public readonly record struct ZipStats(int Count, long Bytes);

public readonly record struct VerifyResult(bool Ok, string Detail);

public sealed class ArchiveService
{
    private const int BufferSize = 80 * 1024;
    private const int ZipMinYear = 1980;
    private const int ZipMaxYear = 2107;

    public ZipStats ZipFiles(
        string root,
        IReadOnlyList<string> files,
        string zipPath,
        CompressionLevel level,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        long bytes = 0;
        var written = 0;

        try
        {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();

                if (!File.Exists(file))
                {
                    continue;
                }

                var rel = (file.Length > prefix.Length + 1 ? file[(prefix.Length + 1)..] : Path.GetFileName(file))
                    .Replace('\\', '/');

                bytes += WriteEntry(zip, file, rel, level, token);
                written++;
                progress?.Report(new(written, rel));
            }

            token.ThrowIfCancellationRequested();
        }
        catch
        {
            SafeDelete(zipPath);
            throw;
        }

        return new(written, bytes);
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

    private static long WriteEntry(ZipArchive zip, string path, string entryName, CompressionLevel level, CancellationToken token)
    {
        var entry = zip.CreateEntry(entryName, level);
        var lastWrite = File.GetLastWriteTime(path);

        entry.LastWriteTime = lastWrite.Year is < ZipMinYear or > ZipMaxYear
            ? new DateTime(ZipMinYear, 1, 1, 0, 0, 0)
            : lastWrite;

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
