using Microsoft.VisualBasic.FileIO;
using System.Diagnostics;
using System.IO.Compression;

namespace SpaceSnoop.Core;

public readonly record struct ZipStats(int Count, long Bytes);

public readonly record struct VerifyResult(bool Ok, string Detail);

public sealed class ArchiveService
{
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

                zip.CreateEntryFromFile(file, rel, level);
                bytes += new FileInfo(file).Length;
                written++;
                progress?.Report(new(written, rel));
            }
        }
        catch
        {
            SafeDelete(zipPath);
            throw;
        }

        return new(written, bytes);
    }

    // TODO: integrity check is structural (central directory readable + count/size match), not per-entry CRC.
    //           Upgrade to streaming every entry through a discard buffer if recycle-bin recoverability isn't enough.
    public VerifyResult VerifyZip(string zipPath, ZipStats expected)
    {
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

        return bytes == expected.Bytes
            ? new(true, string.Empty)
            : new(false, $"объём {SizeFormatter.Format(bytes)} ≠ {SizeFormatter.Format(expected.Bytes)}");
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
