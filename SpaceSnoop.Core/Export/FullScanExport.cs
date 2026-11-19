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
        writer.WriteString("tool", "SpaceSnoop");
        writer.WriteNumber("schema", 2);

        if (version is not null)
        {
            writer.WriteString("version", version);
        }

        writer.WriteString("generatedAt", DateTimeOffset.Now);
        writer.WriteString("path", rootPath);
        writer.WriteBoolean("rootError", root.State == SpaceState.Error);
        writer.WriteStartObject("scan");
        writer.WriteString("engine", options.Engine);
        writer.WriteBoolean("multithreaded", options.Multithreaded);
        writer.WriteNumber("parallelism", options.Parallelism);
        writer.WriteEndObject();

        writer.WriteStartArray("directories");
        var directories = new Stack<(DirectorySpace Directory, string Path, int Depth)>();
        PushDirectories(root, string.Empty, 1, directories);

        while (directories.TryPop(out var item))
        {
            cancel.ThrowIfCancellationRequested();

            if (item.Directory.State == SpaceState.Error)
            {
                errors++;
            }

            writer.WriteStartObject();
            writer.WriteString("path", item.Path);
            writer.WriteNumber("depth", item.Depth);
            writer.WriteNumber("totalBytes", item.Directory.TotalSize);
            writer.WriteNumber("ownBytes", item.Directory.Size);
            writer.WriteNumber("fileCount", item.Directory.TotalFileCount);
            writer.WriteNumber("directoryCount", item.Directory.TotalDirectoryCount);
            writer.WriteBoolean("error", item.Directory.State == SpaceState.Error);
            writer.WriteEndObject();

            completed++;
            ReportProgress(progress, completed, total);
            PushDirectories(item.Directory, item.Path, item.Depth + 1, directories);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("files");
        var pending = new Stack<(DirectorySpace Directory, string Path)>();
        pending.Push((root, string.Empty));

        while (pending.TryPop(out var item))
        {
            cancel.ThrowIfCancellationRequested();

            var fileIndex = 0;

            foreach (var file in item.Directory.Files)
            {
                if ((fileIndex++ & 1023) == 0)
                {
                    cancel.ThrowIfCancellationRequested();
                }

                var path = item.Path.Length == 0 ? file.Name : Path.Combine(item.Path, file.Name);
                writer.WriteStartObject();
                writer.WriteString("path", path);
                writer.WriteNumber("bytes", file.Size);
                writer.WriteEndObject();

                completed++;
                ReportProgress(progress, completed, total);
            }

            PushDirectories(item.Directory, item.Path, pending);
        }

        writer.WriteEndArray();
        writer.WriteStartObject("totals");
        writer.WriteNumber("bytes", root.TotalSize);
        writer.WriteNumber("files", root.TotalFileCount);
        writer.WriteNumber("directories", root.TotalDirectoryCount);
        writer.WriteNumber("errors", errors);
        writer.WriteEndObject();

        if (notes is not null)
        {
            writer.WritePropertyName("notes");
            JsonSerializer.Serialize(writer, notes, ExportJson.Options);
        }

        writer.WriteEndObject();
        writer.Flush();
        progress?.Report(total);
    }

    private static void PushDirectories(
        DirectorySpace parent,
        string path,
        int depth,
        Stack<(DirectorySpace Directory, string Path, int Depth)> destination)
    {
        for (var index = parent.SubDirectories.Count - 1; index >= 0; index--)
        {
            var directory = parent.SubDirectories[index];
            var childPath = path.Length == 0 ? directory.Name : Path.Combine(path, directory.Name);
            destination.Push((directory, childPath, depth));
        }
    }

    private static void PushDirectories(
        DirectorySpace parent,
        string path,
        Stack<(DirectorySpace Directory, string Path)> destination)
    {
        for (var index = parent.SubDirectories.Count - 1; index >= 0; index--)
        {
            var directory = parent.SubDirectories[index];
            var childPath = path.Length == 0 ? directory.Name : Path.Combine(path, directory.Name);
            destination.Push((directory, childPath));
        }
    }

    private static void ReportProgress(IProgress<int>? progress, int completed, int total)
    {
        if (completed % 4096 == 0 || completed == total)
        {
            progress?.Report(completed);
        }
    }
}
