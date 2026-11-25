using System.Buffers;
using System.Text.Json;

namespace SpaceSnoop.Core.Export;

public static class FullScanExport
{
    public static void WriteJson(
        Stream output,
        DirectorySpace root,
        string rootPath,
        ScanExportOptions options,
        string? version,
        ScanExportNotes? notes,
        IProgress<int>? progress,
        CancellationToken cancel)
    {
        using var writer = new Utf8JsonWriter(
            output,
            new()
            {
                Encoder = ExportJson.Options.Encoder,
            });

        var total = root.TotalFileCount + root.TotalDirectoryCount;
        var completed = 0;
        var errors = root.State == SpaceState.Error ? 1 : 0;

        writer.WriteStartObject();
        writer.WriteString("tool"u8, "SpaceSnoop"u8);
        writer.WriteNumber("schema"u8, 2);

        if (version is not null)
        {
            writer.WriteString("version"u8, version);
        }

        writer.WriteString("generatedAt"u8, DateTimeOffset.Now);
        writer.WriteString("path"u8, rootPath);
        writer.WriteBoolean("rootError"u8, root.State == SpaceState.Error);
        writer.WriteStartObject("scan"u8);
        writer.WriteString("engine"u8, options.Engine);
        writer.WriteBoolean("multithreaded"u8, options.Multithreaded);
        writer.WriteNumber("parallelism"u8, options.Parallelism);
        writer.WriteEndObject();

        var pathBuffer = ArrayPool<char>.Shared.Rent(4096);

        try
        {
            WriteDirectoriesSection(writer, root, ref pathBuffer, ref errors, ref completed, total, progress, cancel);
            WriteFilesSection(writer, root, ref pathBuffer, ref completed, total, progress, cancel);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(pathBuffer);
        }

        writer.WriteStartObject("totals"u8);
        writer.WriteNumber("bytes"u8, root.TotalSize);
        writer.WriteNumber("files"u8, root.TotalFileCount);
        writer.WriteNumber("directories"u8, root.TotalDirectoryCount);
        writer.WriteNumber("errors"u8, errors);
        writer.WriteEndObject();

        if (notes is not null)
        {
            writer.WritePropertyName("notes"u8);
            JsonSerializer.Serialize(writer, notes, ExportJson.Options);
        }

        writer.WriteEndObject();
        writer.Flush();
        progress?.Report(total);
    }

    private static void WriteDirectoriesSection(
        Utf8JsonWriter writer,
        DirectorySpace root,
        ref char[] pathBuffer,
        ref int errors,
        ref int completed,
        int total,
        IProgress<int>? progress,
        CancellationToken cancel)
    {
        writer.WriteStartArray("directories"u8);

        if (root.SubDirectoriesArray.Length > 0)
        {
            var stack = new Stack<TraversalFrame>();
            stack.Push(new(root, 0, 1));

            while (stack.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();

                var frame = stack.Pop();
                var subs = frame.Directory.SubDirectoriesArray;

                if (frame.ChildIndex >= subs.Length)
                {
                    continue;
                }

                var child = subs[frame.ChildIndex];

                if (frame.ChildIndex + 1 < subs.Length)
                {
                    stack.Push(frame with { ChildIndex = frame.ChildIndex + 1 });
                }

                var childPathLen = AppendPathSegment(ref pathBuffer, frame.PathLength, child.Name);

                if (child.State == SpaceState.Error)
                {
                    errors++;
                }

                writer.WriteStartObject();
                writer.WriteString("path"u8, pathBuffer.AsSpan(0, childPathLen));
                writer.WriteNumber("depth"u8, frame.Depth);
                writer.WriteNumber("totalBytes"u8, child.TotalSize);
                writer.WriteNumber("ownBytes"u8, child.Size);
                writer.WriteNumber("fileCount"u8, child.TotalFileCount);
                writer.WriteNumber("directoryCount"u8, child.TotalDirectoryCount);
                writer.WriteBoolean("error"u8, child.State == SpaceState.Error);
                writer.WriteEndObject();

                completed++;
                ReportProgress(progress, completed, total);

                if (child.SubDirectoriesArray.Length > 0)
                {
                    stack.Push(new(child, childPathLen, frame.Depth + 1));
                }
            }
        }

        writer.WriteEndArray();
    }

    private static void WriteFilesSection(
        Utf8JsonWriter writer,
        DirectorySpace root,
        ref char[] pathBuffer,
        ref int completed,
        int total,
        IProgress<int>? progress,
        CancellationToken cancel)
    {
        writer.WriteStartArray("files"u8);

        WriteDirectoryFiles(writer, root, 0, ref pathBuffer, ref completed, total, progress, cancel);

        if (root.SubDirectoriesArray.Length > 0)
        {
            var stack = new Stack<TraversalFrame>();
            stack.Push(new(root, 0, 1));

            while (stack.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();

                var frame = stack.Pop();
                var subs = frame.Directory.SubDirectoriesArray;

                if (frame.ChildIndex >= subs.Length)
                {
                    continue;
                }

                var child = subs[frame.ChildIndex];

                if (frame.ChildIndex + 1 < subs.Length)
                {
                    stack.Push(frame with { ChildIndex = frame.ChildIndex + 1 });
                }

                var childPathLen = AppendPathSegment(ref pathBuffer, frame.PathLength, child.Name);
                WriteDirectoryFiles(writer, child, childPathLen, ref pathBuffer, ref completed, total, progress, cancel);

                if (child.SubDirectoriesArray.Length > 0)
                {
                    stack.Push(new(child, childPathLen, frame.Depth + 1));
                }
            }
        }

        writer.WriteEndArray();
    }

    private static void WriteDirectoryFiles(
        Utf8JsonWriter writer,
        DirectorySpace directory,
        int dirPathLen,
        ref char[] pathBuffer,
        ref int completed,
        int total,
        IProgress<int>? progress,
        CancellationToken cancel)
    {
        var fileEntries = directory.FileEntries;

        for (var i = 0; i < fileEntries.Length; i++)
        {
            if ((i & 1023) == 0)
            {
                cancel.ThrowIfCancellationRequested();
            }

            var fileUtf8 = directory.GetFileNameUtf8(i);
            var fullLen = AppendUtf8Segment(ref pathBuffer, dirPathLen, fileUtf8);

            writer.WriteStartObject();
            writer.WriteString("path"u8, pathBuffer.AsSpan(0, fullLen));
            writer.WriteNumber("bytes"u8, fileEntries[i].Size);
            writer.WriteEndObject();

            completed++;
            ReportProgress(progress, completed, total);
        }
    }

    private static int AppendPathSegment(ref char[] buffer, int prefixLen, string name)
    {
        if (prefixLen == 0)
        {
            EnsureCapacity(ref buffer, name.Length);
            name.CopyTo(buffer);
            return name.Length;
        }

        if (Path.IsPathRooted(name))
        {
            var combined = Path.Combine(new string(buffer, 0, prefixLen), name);
            EnsureCapacity(ref buffer, combined.Length);
            combined.CopyTo(buffer);
            return combined.Length;
        }

        var needsSeparator = !IsDirectorySeparator(buffer[prefixLen - 1]);
        var totalLen = prefixLen + (needsSeparator ? 1 : 0) + name.Length;
        EnsureCapacity(ref buffer, totalLen);

        var pos = prefixLen;

        if (needsSeparator)
        {
            buffer[pos++] = Path.DirectorySeparatorChar;
        }

        name.CopyTo(buffer.AsSpan(pos));
        return totalLen;
    }

    private static int AppendUtf8Segment(ref char[] buffer, int prefixLen, ReadOnlySpan<byte> utf8Name)
    {
        if (prefixLen == 0)
        {
            EnsureCapacity(ref buffer, utf8Name.Length);
            return Wtf8.ToUtf16(utf8Name, buffer);
        }

        var needsSeparator = !IsDirectorySeparator(buffer[prefixLen - 1]);
        var maxLen = prefixLen + (needsSeparator ? 1 : 0) + utf8Name.Length;
        EnsureCapacity(ref buffer, maxLen);

        var pos = prefixLen;

        if (needsSeparator)
        {
            buffer[pos++] = Path.DirectorySeparatorChar;
        }

        var written = Wtf8.ToUtf16(utf8Name, buffer.AsSpan(pos));
        return pos + written;
    }

    private static bool IsDirectorySeparator(char c)
    {
        return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
    }

    private static void EnsureCapacity(ref char[] buffer, int required)
    {
        if (required <= buffer.Length)
        {
            return;
        }

        var nextSize = Math.Max(buffer.Length * 2, required);
        var next = ArrayPool<char>.Shared.Rent(nextSize);
        buffer.CopyTo(next, 0);
        ArrayPool<char>.Shared.Return(buffer);
        buffer = next;
    }

    private static void ReportProgress(IProgress<int>? progress, int completed, int total)
    {
        if (completed % 4096 == 0 || completed == total)
        {
            progress?.Report(completed);
        }
    }

    private readonly record struct TraversalFrame(DirectorySpace Directory, int PathLength, int Depth, int ChildIndex = 0);
}
