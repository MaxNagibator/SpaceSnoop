using System.Text.Json;

namespace SpaceSnoop.Core.Export;

public sealed record ScanExportOptions(int Depth, bool Multithreaded, int Parallelism, string Engine = ScanExportOptions.DirectoryEngine)
{
    public const string DirectoryEngine = "directories";
    public const string MftEngine = "mft";
}

/// <summary>Поправки к итогу скана: не сосчитанное повторно и не попавшее в дерево.</summary>
public sealed record ScanExportNotes(
    long ExtraNameBytes,
    long DroppedObjects,
    long DroppedBytes,
    long UnknownSizeFiles,
    long PartialRecords)
{
    public static ScanExportNotes? From(
        long extraNameBytes,
        long droppedObjects,
        long droppedBytes,
        long unknownSizeFiles,
        long partialRecords)
    {
        return extraNameBytes > 0 || droppedObjects > 0 || droppedBytes > 0 || unknownSizeFiles > 0 || partialRecords > 0
            ? new(extraNameBytes, droppedObjects, droppedBytes, unknownSizeFiles, partialRecords)
            : null;
    }
}

public sealed record ScanExportDirectory(
    string Path,
    int Depth,
    long TotalBytes,
    long OwnBytes,
    int FileCount,
    int DirectoryCount,
    bool Error);

public sealed record ScanExportFile(string Path, long Bytes);

public sealed record ScanExportTotals(long Bytes, int Files, int Directories, int Errors);

public sealed record ScanExportModel
{
    public string Tool { get; init; } = "SpaceSnoop";
    public int Schema { get; init; } = 1;
    public string? Version { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public required string Path { get; init; }
    public required ScanExportOptions Options { get; init; }
    public required ScanExportTotals Totals { get; init; }
    public IReadOnlyList<ScanExportDirectory> Directories { get; init; } = [];
    public IReadOnlyList<ScanExportFile> Files { get; init; } = [];
    public IReadOnlyList<string> ErrorPaths { get; init; } = [];
    public ScanExportNotes? Notes { get; init; }
    public int OmittedDirectories { get; init; }
    public int OmittedFiles { get; init; }
}

/// <summary>
/// Машиночитаемый дамп результата сканирования: крупнейшие подкаталоги до заданной глубины
/// и крупнейшие файлы всего дерева. Отвечает на вопрос «куда делось место», поэтому усечение
/// идёт по размеру, а отброшенное честно объявляется счётчиками.
/// </summary>
public static class ScanExport
{
    public const int DefaultEntryLimit = 200;
    public const int DefaultDepth = 2;
    public const int MinDepth = 1;
    public const int MaxDepth = 8;

    private const int ErrorPathLimit = 50;
    private const string RootDirectoryName = ".";

    public static ScanExportModel Build(
        DirectorySpace root,
        string rootPath,
        ScanExportOptions options,
        string? version = null,
        int entryLimit = DefaultEntryLimit)
    {
        var context = new Context(Math.Clamp(options.Depth, MinDepth, MaxDepth), Math.Max(1, entryLimit));
        var counts = Walk(root, root, 1, context);

        var directories = DrainDirectories(context.Directories);
        var files = DrainFiles(root, context.Files);

        return new()
        {
            Version = version,
            GeneratedAt = DateTimeOffset.Now,
            Path = rootPath,
            Options = options with
            {
                Depth = context.Depth,
            },
            Totals = new(root.TotalSize, counts.Files, counts.Directories, context.Errors),
            Directories = directories,
            Files = files,
            ErrorPaths = context.ErrorPaths,
            OmittedDirectories = context.DirectoriesSeen - directories.Count,
            OmittedFiles = counts.Files - files.Count,
        };
    }

    public static string ToJson(ScanExportModel model)
    {
        return JsonSerializer.Serialize(model, ExportJson.Options);
    }

    /// <summary>
    /// Обходит дерево один раз: считает файлы и каталоги снизу вверх (собственные счётчики
    /// <see cref="DirectorySpace" /> пересчитывали бы поддерево на каждом узле) и попутно
    /// набирает крупнейшие записи.
    /// </summary>
    private static Counts Walk(DirectorySpace root, DirectorySpace directory, int depth, Context context)
    {
        if (directory.State == SpaceState.Error)
        {
            context.Errors++;

            if (context.ErrorPaths.Count < ErrorPathLimit)
            {
                var relative = BuildRelativePath(root, directory);
                context.ErrorPaths.Add(relative.Length == 0 ? RootDirectoryName : relative);
            }
        }

        var fileEntries = directory.FileEntries;
        var files = fileEntries.Length;

        for (var i = 0; i < fileEntries.Length; i++)
        {
            var size = fileEntries[i].Size;

            if (context.Files.Count < context.Limit)
            {
                context.Files.Enqueue(new(directory, i, size), size);
            }
            else if (context.Files.TryPeek(out _, out var min) && size > min)
            {
                context.Files.EnqueueDequeue(new(directory, i, size), size);
            }
        }

        var subs = directory.SubDirectoriesArray;
        var directories = 0;

        for (var i = 0; i < subs.Length; i++)
        {
            var sub = subs[i];
            var counts = Walk(root, sub, depth + 1, context);

            files += counts.Files;
            directories += counts.Directories + 1;

            if (depth > context.Depth)
            {
                continue;
            }

            context.DirectoriesSeen++;
            var totalSize = sub.TotalSize;

            if (context.Directories.Count < context.Limit)
            {
                context.Directories.Enqueue(
                    new(BuildRelativePath(root, sub), depth, totalSize, sub.Size, counts.Files, counts.Directories, sub.State == SpaceState.Error),
                    totalSize);
            }
            else if (context.Directories.TryPeek(out _, out var min) && totalSize > min)
            {
                context.Directories.EnqueueDequeue(
                    new(BuildRelativePath(root, sub), depth, totalSize, sub.Size, counts.Files, counts.Directories, sub.State == SpaceState.Error),
                    totalSize);
            }
        }

        return new(files, directories);
    }

    private static string BuildRelativePath(DirectorySpace root, DirectorySpace target)
    {
        if (ReferenceEquals(target, root))
        {
            return string.Empty;
        }

        var depth = 0;

        for (SpaceBase? current = target; current is not null && !ReferenceEquals(current, root); current = current.Parent)
        {
            depth++;
        }

        if (depth == 1)
        {
            return target.Name;
        }

        var segments = new string[depth];
        var index = depth;

        for (SpaceBase? current = target; current is not null && !ReferenceEquals(current, root); current = current.Parent)
        {
            segments[--index] = current.Name;
        }

        return Path.Combine(segments);
    }

    private static List<ScanExportDirectory> DrainDirectories(PriorityQueue<ScanExportDirectory, long> queue)
    {
        var items = new List<ScanExportDirectory>(queue.Count);

        while (queue.TryDequeue(out var item, out _))
        {
            items.Add(item);
        }

        items.Sort(static (left, right) => right.TotalBytes.CompareTo(left.TotalBytes));
        return items;
    }

    private static List<ScanExportFile> DrainFiles(DirectorySpace root, PriorityQueue<FileCandidate, long> queue)
    {
        var items = new List<ScanExportFile>(queue.Count);

        while (queue.TryDequeue(out var item, out _))
        {
            var directoryPath = BuildRelativePath(root, item.Directory);
            var fileName = item.Directory.GetFileName(item.FileIndex);
            var path = directoryPath.Length == 0
                ? fileName
                : Path.Combine(directoryPath, fileName);

            items.Add(new(path, item.Size));
        }

        items.Sort(static (left, right) => right.Bytes.CompareTo(left.Bytes));
        return items;
    }

    private readonly record struct Counts(int Files, int Directories);

    private readonly record struct FileCandidate(DirectorySpace Directory, int FileIndex, long Size);

    private sealed class Context(int depth, int limit)
    {
        public int Depth { get; } = depth;

        public int Limit { get; } = limit;

        public PriorityQueue<ScanExportDirectory, long> Directories { get; } = new();

        public PriorityQueue<FileCandidate, long> Files { get; } = new();

        public List<string> ErrorPaths { get; } = [];

        public int Errors { get; set; }

        public int DirectoriesSeen { get; set; }
    }
}
