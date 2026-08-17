using System.Text.Json;

namespace SpaceSnoop.Core.Export;

public sealed record ScanExportOptions(int Depth, bool Multithreaded, int Parallelism, string Engine = ScanExportOptions.DirectoryEngine)
{
    public const string DirectoryEngine = "directories";
    public const string MftEngine = "mft";
}

/// <summary>Поправки к итогу скана: не сосчитанное повторно и не попавшее в дерево.</summary>
public sealed record ScanExportNotes(long ExtraNameBytes, long DroppedObjects, long DroppedBytes, long UnknownSizeFiles)
{
    public static ScanExportNotes? From(long extraNameBytes, long droppedObjects, long droppedBytes, long unknownSizeFiles)
    {
        return extraNameBytes > 0 || droppedObjects > 0 || droppedBytes > 0 || unknownSizeFiles > 0
            ? new(extraNameBytes, droppedObjects, droppedBytes, unknownSizeFiles)
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
        var counts = Walk(root, string.Empty, 1, context);

        var directories = DrainDirectories(context.Directories);
        var files = DrainFiles(context.Files);

        return new()
        {
            Version = version,
            GeneratedAt = DateTimeOffset.Now,
            Path = rootPath,
            Options = options with { Depth = context.Depth },
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
    private static Counts Walk(DirectorySpace directory, string relative, int depth, Context context)
    {
        if (directory.State == SpaceState.Error)
        {
            context.Errors++;

            if (context.ErrorPaths.Count < ErrorPathLimit)
            {
                context.ErrorPaths.Add(relative.Length == 0 ? RootDirectoryName : relative);
            }
        }

        var files = directory.Files.Count;
        var directories = 0;

        foreach (var file in directory.Files)
        {
            Push(context.Files, new FileEntry(relative, file), file.Size, context.Limit);
        }

        foreach (var sub in directory.SubDirectories)
        {
            var path = relative.Length == 0 ? sub.Name : Path.Combine(relative, sub.Name);
            var counts = Walk(sub, path, depth + 1, context);

            files += counts.Files;
            directories += counts.Directories + 1;

            if (depth > context.Depth)
            {
                continue;
            }

            context.DirectoriesSeen++;

            Push(
                context.Directories,
                new ScanExportDirectory(path, depth, sub.TotalSize, sub.Size, counts.Files, counts.Directories, sub.State == SpaceState.Error),
                sub.TotalSize,
                context.Limit);
        }

        return new(files, directories);
    }

    /// <summary>
    /// Держит в очереди только <paramref name="limit" /> крупнейших элементов: наименьший
    /// вытесняется сразу, поэтому дерево с миллионом файлов не материализуется списком.
    /// </summary>
    private static void Push<T>(PriorityQueue<T, long> queue, T item, long weight, int limit)
    {
        queue.Enqueue(item, weight);

        if (queue.Count > limit)
        {
            queue.Dequeue();
        }
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

    private static List<ScanExportFile> DrainFiles(PriorityQueue<FileEntry, long> queue)
    {
        var items = new List<ScanExportFile>(queue.Count);

        while (queue.TryDequeue(out var item, out _))
        {
            var path = item.Directory.Length == 0
                ? item.File.Name
                : Path.Combine(item.Directory, item.File.Name);

            items.Add(new(path, item.File.Size));
        }

        items.Sort(static (left, right) => right.Bytes.CompareTo(left.Bytes));
        return items;
    }

    private readonly record struct Counts(int Files, int Directories);

    private readonly record struct FileEntry(string Directory, FileSpace File);

    private sealed class Context(int depth, int limit)
    {
        public int Depth { get; } = depth;

        public int Limit { get; } = limit;

        public PriorityQueue<ScanExportDirectory, long> Directories { get; } = new();

        public PriorityQueue<FileEntry, long> Files { get; } = new();

        public List<string> ErrorPaths { get; } = [];

        public int Errors { get; set; }

        public int DirectoriesSeen { get; set; }
    }
}
