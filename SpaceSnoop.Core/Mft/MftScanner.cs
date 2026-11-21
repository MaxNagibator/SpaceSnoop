using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Domain;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core.Mft;

public sealed class MftScanner(ILogger<MftScanner>? logger = null)
{
    private readonly ILogger _log = logger ?? NullLogger<MftScanner>.Instance;

    public static MftAvailability Probe(string path)
    {
        return MftReader.Probe(path);
    }

    // TODO: $MFT читается целиком независимо от цели скана, поэтому на подкаталоге движок проигрывает
    // обходу каталогами (`C:\Windows` – 2,77 с против 3,18 с); сам выбор отдан вызывающему (в WPF – настройкой),
    // здесь по цели не решают. Триггер апгрейда: понадобится автоматика – решать по доле цели в томе.
    public MftScanResult Calculate(DirectoryInfo directory, ScanProgress? progress, CancellationToken cancel = default)
    {
        var letter = MftReader.VolumeLetter(directory.FullName)
            ?? throw new InvalidOperationException($"Путь {directory.FullName} не лежит на локальном томе с буквой");

        using var volume = MftFileVolume.Open(letter);
        return Calculate(volume, directory, letter, MftReader.ResolveThreads(letter), 4 * 1024 * 1024, progress, cancel);
    }

    internal MftScanResult Calculate(
        IMftVolume volume,
        DirectoryInfo directory,
        char letter,
        int threads,
        int blockBytes,
        ScanProgress? progress,
        CancellationToken cancel = default)
    {
        var table = MftReader.Read(volume, letter, threads, blockBytes, progress, cancel);

        progress?.Announce($"Разбор связей тома {letter}:");

        var links = MftLinks.Build(table, cancel);
        var start = Locate(table, links, directory, letter);

        if (start < 0)
        {
            throw new DirectoryNotFoundException($"Каталог {directory.FullName} не найден в $MFT тома {letter}:");
        }

        links.Rehome(table, start, cancel);
        links.DropUnreachable(table, cancel);

        var totals = new MftSubtreeTotals();
        var root = Materialize(table, links, start, directory, totals, progress, cancel);
        root.AggregateTotals();
        root.FixAbsolutePath(directory);

        var statistics = table.Statistics;

        var result = new MftScanResult(
            root,
            totals.ExtraNames,
            totals.ExtraNameBytes,
            totals.UnknownSizeFiles,
            statistics.SkippedLinks,
            statistics.Detached,
            statistics.OrphanFiles,
            statistics.OrphanBytes,
            statistics.Nameless,
            statistics.NamelessBytes,
            statistics.Damaged,
            statistics.Partial,
            statistics.StaleParents,
            statistics.Rehomed,
            threads,
            FormatReport(statistics, totals.UnknownSizeFiles));

        _log.MftScanCompleted(directory.FullName, table.Entries.Length, result.ExtraNames, result.ExtraNameBytes, result.Report);

        return result;
    }

    internal static string FormatReport(MftStatistics statistics, long unknownSizeFiles)
    {
        var report =
            $"записей {statistics.RecordsScanned:N0}, занято {statistics.RecordsInUse:N0}, расширений {statistics.Extensions:N0}, "
            + $"с несколькими именами {statistics.HardLinkedFiles:N0}, пропущено ссылок {statistics.SkippedLinks:N0}, "
            + $"безымянных {statistics.Nameless:N0}, оторванных {statistics.Detached:N0}, повреждённых {statistics.Damaged:N0}, "
            + $"неполных {statistics.Partial:N0}, без размера {unknownSizeFiles:N0}, "
            + $"устаревших ссылок на родителя {statistics.StaleParents:N0}, переподвешено {statistics.Rehomed:N0}";

        if (statistics.Detached > 0 || statistics.MissingParents > 0 || statistics.Unreachable > 0)
        {
            report += $", без записи родителя {statistics.MissingParents:N0}, в оторванных ветках {statistics.Unreachable:N0}";
        }

        if (statistics.DetachedSamples.Count > 0)
        {
            report += $" (примеры: {string.Join(", ", statistics.DetachedSamples.Select(static sample => sample.Text))})";
        }

        return report;
    }

    internal static int Locate(MftTable table, MftLinks links, DirectoryInfo directory, char letter)
    {
        var entries = table.Entries;

        if (entries.Length <= MftLayout.RootRecord
            || !entries[MftLayout.RootRecord].Exists
            || !entries[MftLayout.RootRecord].IsDirectory)
        {
            return -1;
        }

        var full = Path.GetFullPath(directory.FullName);
        var root = Path.GetPathRoot(full) ?? $"{letter}:\\";
        var relative = full[root.Length..].Trim(Path.DirectorySeparatorChar);
        var current = MftLayout.RootRecord;

        if (relative.Length == 0)
        {
            return current;
        }

        // TODO: чувствительность к регистру снимается один раз с корня тома, поэтому каталог с пофайловым
        // режимом NTFS (`fsutil file setCaseSensitiveInfo`) получает политику корня; точное совпадение имени
        // предпочитается нечувствительному, и этого хватает, пока обе формы имени существуют рядом.
        // Триггер апгрейда: понадобится отличать `foo` от `Foo`, когда лежит только одна из них.
        var caseSensitive = PathCase.IsCaseSensitive(root);

        foreach (var range in relative.AsSpan().Split(Path.DirectorySeparatorChar))
        {
            var segment = relative.AsSpan(range);

            if (segment.IsEmpty)
            {
                continue;
            }

            var found = -1;
            var exact = -1;

            for (var child = links.FirstChild[current]; child >= 0; child = links.NextSibling[child])
            {
                if (!entries[child].IsDirectory)
                {
                    continue;
                }

                if (table.Names.Equals(entries[child].Name, segment, StringComparison.Ordinal))
                {
                    exact = child;
                    break;
                }

                if (!caseSensitive
                    && found < 0
                    && table.Names.Equals(entries[child].Name, segment, StringComparison.OrdinalIgnoreCase))
                {
                    found = child;
                }
            }

            current = exact >= 0 ? exact : found;

            if (current < 0)
            {
                return -1;
            }
        }

        return current;
    }

    internal static DirectorySpace Materialize(
        MftTable table,
        MftLinks links,
        int start,
        DirectoryInfo directory,
        MftSubtreeTotals totals,
        ScanProgress? progress,
        CancellationToken cancel)
    {
        var entries = table.Entries;
        var store = new ScanTreeStore(1, singleWriter: true);
        var writer = new FileSlabWriter(store);
        var root = new DirectorySpace(
            store,
            store.Names.Intern(directory.Name),
            null,
            TimestampEncoding.FromMftFileTime(entries[start].CreationFileTime),
            TimestampEncoding.FromMftFileTime(entries[start].LastAccessFileTime),
            FileEntry.UtcFileTimeFlag);

        var branchCount = 0;

        for (var child = links.FirstChild[start]; child >= 0; child = links.NextSibling[child])
        {
            if (entries[child].IsDirectory)
            {
                branchCount++;
            }
        }

        progress?.SetTopLevelTotal(branchCount);

        var stack = new Stack<(int Record, DirectorySpace Node)>();
        var paths = new MftProgressPath(root, directory.FullName);
        var files = new List<FileEntry>();
        var branches = new List<(int Record, DirectorySpace Node)>();
        var children = new List<(int Record, DirectorySpace Node)>();

        FillDirectory(table, links, start, root, store, writer, paths, totals, branches, files, progress);

        for (var i = 0; i < branches.Count; i++)
        {
            stack.Push(branches[i]);

            while (stack.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();

                var (record, node) = stack.Pop();
                FillDirectory(table, links, record, node, store, writer, paths, totals, children, files, progress);

                for (var j = 0; j < children.Count; j++)
                {
                    stack.Push(children[j]);
                }
            }

            progress?.CompleteTopLevel();
        }

        store.Freeze();
        return root;
    }

    private static void FillDirectory(
        MftTable table,
        MftLinks links,
        int record,
        DirectorySpace node,
        ScanTreeStore store,
        FileSlabWriter writer,
        MftProgressPath paths,
        MftSubtreeTotals totals,
        List<(int Record, DirectorySpace Node)> children,
        List<FileEntry> files,
        ScanProgress? progress)
    {
        progress?.EnterDirectory(paths.For(node));

        var entries = table.Entries;
        children.Clear();
        files.Clear();
        var ownSize = 0L;

        for (var child = links.FirstChild[record]; child >= 0; child = links.NextSibling[child])
        {
            ref var entry = ref entries[child];
            var names = Math.Max(1, (int)entry.Names);

            if (names > 1)
            {
                totals.ExtraNames += names - 1;
                totals.ExtraNameBytes += entry.Size * (names - 1);
            }

            var nameId = store.Names.Intern(table.Names.GetUtf8(entry.Name));

            if (entry.IsDirectory)
            {
                children.Add(
                    (child,
                     new(
                         store,
                         nameId,
                         node,
                         TimestampEncoding.FromMftFileTime(entry.CreationFileTime),
                         TimestampEncoding.FromMftFileTime(entry.LastAccessFileTime),
                         FileEntry.UtcFileTimeFlag)));
                continue;
            }

            if (!entry.SizeKnown)
            {
                totals.UnknownSizeFiles++;
            }

            files.Add(
                new()
                {
                    Size = entry.Size,
                    CreationRaw = TimestampEncoding.FromMftFileTime(entry.CreationFileTime),
                    LastAccessRaw = TimestampEncoding.FromMftFileTime(entry.LastAccessFileTime),
                    NameId = nameId,
                    State = SpaceState.Added,
                    Flags = FileEntry.UtcFileTimeFlag,
                });
            ownSize += entry.Size;
        }

        var subDirectories = children.Count == 0 ? [] : new DirectorySpace[children.Count];

        for (var i = 0; i < subDirectories.Length; i++)
        {
            subDirectories[i] = children[i].Node;
        }

        writer.WriteFiles(CollectionsMarshal.AsSpan(files), out var fileStart);
        node.SetScanned(store, fileStart, files.Count, ownSize, subDirectories);
        progress?.AddFiles(files.Count, node.Size);
    }
}

internal sealed class MftProgressPath(DirectorySpace root, string rootPath)
{
    private const long RefreshMilliseconds = 50;

    private readonly List<string?> _segments = [];
    private string _current = rootPath;
    private long _due;

    public string For(DirectorySpace node)
    {
        var now = Environment.TickCount64;

        if (now < _due)
        {
            return _current;
        }

        _due = now + RefreshMilliseconds;
        _current = Build(node);
        return _current;
    }

    private string Build(DirectorySpace node)
    {
        _segments.Clear();

        for (SpaceBase? current = node; current is not null && !ReferenceEquals(current, root); current = current.Parent)
        {
            _segments.Add(current.Name);
        }

        if (_segments.Count == 0)
        {
            return rootPath;
        }

        _segments.Add(rootPath);
        _segments.Reverse();

        return Path.Join(CollectionsMarshal.AsSpan(_segments));
    }
}

internal sealed class MftSubtreeTotals
{
    public long ExtraNames { get; set; }

    public long ExtraNameBytes { get; set; }

    public long UnknownSizeFiles { get; set; }
}

public sealed record MftScanResult(
    DirectorySpace Root,
    long ExtraNames,
    long ExtraNameBytes,
    long UnknownSizeFiles,
    long SkippedLinks,
    long DetachedRecords,
    long OrphanFiles,
    long OrphanBytes,
    long NamelessRecords,
    long NamelessBytes,
    long DamagedRecords,
    long PartialRecords,
    long StaleParents,
    long Rehomed,
    int Threads,
    string Report)
{
    public long DroppedObjects => DetachedRecords + NamelessRecords + DamagedRecords;

    public long DroppedBytes => OrphanBytes + NamelessBytes;

    public bool IsComplete => DroppedObjects == 0 && PartialRecords == 0 && UnknownSizeFiles == 0;
}
