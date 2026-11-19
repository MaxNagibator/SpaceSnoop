using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

        var table = MftReader.Read(letter, progress, cancel);

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
            MftReader.ResolveThreads(letter),
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
        var comparer = PathCase.ComparerFor(root);

        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var found = -1;
            var exact = -1;

            for (var child = links.FirstChild[current]; child >= 0; child = links.NextSibling[child])
            {
                if (!entries[child].IsDirectory)
                {
                    continue;
                }

                if (string.Equals(entries[child].Name, segment, StringComparison.Ordinal))
                {
                    exact = child;
                    break;
                }

                if (found < 0 && comparer.Equals(entries[child].Name, segment))
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

    private static DirectorySpace Materialize(
        MftTable table,
        MftLinks links,
        int start,
        DirectoryInfo directory,
        MftSubtreeTotals totals,
        ScanProgress? progress,
        CancellationToken cancel)
    {
        var entries = table.Entries;
        var root = new DirectorySpace(directory.Name, null, entries[start].CreationTime, entries[start].LastAccessTime);
        var branches = 0;

        for (var child = links.FirstChild[start]; child >= 0; child = links.NextSibling[child])
        {
            if (entries[child].IsDirectory)
            {
                branches++;
            }
        }

        progress?.SetTopLevelTotal(branches);

        var stack = new Stack<(int Record, DirectorySpace Node)>();
        var paths = new MftProgressPath(root, directory.FullName);
        var files = new List<FileSpace>();
        var roots = FillDirectory(table, links, start, root, paths, totals, files, progress);

        foreach (var branch in roots)
        {
            stack.Push(branch);

            while (stack.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();

                var (record, node) = stack.Pop();

                foreach (var child in FillDirectory(table, links, record, node, paths, totals, files, progress))
                {
                    stack.Push(child);
                }
            }

            progress?.CompleteTopLevel();
        }

        return root;
    }

    private static List<(int Record, DirectorySpace Node)> FillDirectory(
        MftTable table,
        MftLinks links,
        int record,
        DirectorySpace node,
        MftProgressPath paths,
        MftSubtreeTotals totals,
        List<FileSpace> files,
        ScanProgress? progress)
    {
        progress?.EnterDirectory(paths.For(node));

        var entries = table.Entries;
        var children = new List<(int, DirectorySpace)>();
        files.Clear();

        for (var child = links.FirstChild[record]; child >= 0; child = links.NextSibling[child])
        {
            ref var entry = ref entries[child];
            var names = Math.Max(1, entry.Names);

            if (names > 1)
            {
                totals.ExtraNames += names - 1;
                totals.ExtraNameBytes += entry.Size * (names - 1);
            }

            if (entry.IsDirectory)
            {
                children.Add((child, new(entry.Name!, node, entry.CreationTime, entry.LastAccessTime)));
                continue;
            }

            if (!entry.SizeKnown)
            {
                totals.UnknownSizeFiles++;
            }

            files.Add(FileSpace.Create(new ScanEntry(entry.Name!, entry.Size, entry.CreationTime, entry.LastAccessTime, FileAttributes.Normal, false), node));
        }

        var subDirectories = children.Count == 0 ? [] : new DirectorySpace[children.Count];

        for (var i = 0; i < subDirectories.Length; i++)
        {
            subDirectories[i] = children[i].Item2;
        }

        node.SetScanned(files, subDirectories);
        progress?.AddFiles(files.Count, node.Size);

        return children;
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
