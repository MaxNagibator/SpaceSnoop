using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SpaceSnoop.Core.Mft;

public sealed class MftScanner(ILogger<MftScanner>? logger = null)
{
    private readonly ILogger _log = logger ?? NullLogger<MftScanner>.Instance;

    public static MftAvailability Probe(string path)
    {
        return MftReader.Probe(path);
    }

    // TODO: $MFT читается целиком независимо от цели скана, поэтому на подкаталоге движок проигрывает
    // обходу каталогами (`C:\Windows` – 2,77 с против 4,26 с) и выбор по цели не сделан. Триггер апгрейда:
    // появится сценарий, где сканируют один подкаталог часто – тогда решать по доле цели в томе.
    public MftScanResult Calculate(DirectoryInfo directory, ScanProgress? progress, CancellationToken cancel = default)
    {
        var letter = MftReader.VolumeLetter(directory.FullName)
                     ?? throw new InvalidOperationException($"Путь {directory.FullName} не лежит на локальном томе с буквой");

        var table = MftReader.Read(letter, progress, cancel);
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

        var result = new MftScanResult(root,
            totals.ExtraNames,
            totals.ExtraNameBytes,
            totals.UnknownSizeFiles,
            statistics.SkippedLinks,
            statistics.Detached,
            statistics.OrphanFiles,
            statistics.OrphanBytes,
            statistics.Nameless,
            statistics.Damaged,
            statistics.StaleParents,
            statistics.Rehomed,
            $"записей {statistics.RecordsScanned:N0}, занято {statistics.RecordsInUse:N0}, расширений {statistics.Extensions:N0}, " +
            $"безымянных {statistics.Nameless:N0}, оторванных {statistics.Detached:N0}, повреждённых {statistics.Damaged:N0}, " +
            $"устаревших ссылок на родителя {statistics.StaleParents:N0}, переподвешено {statistics.Rehomed:N0}");

        _log.MftScanCompleted(directory.FullName, table.Entries.Length, result.ExtraNames, result.ExtraNameBytes, result.Report);

        return result;
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

        var comparer = PathCase.ComparerFor(root);

        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var found = -1;

            for (var child = links.FirstChild[current]; child >= 0; child = links.NextSibling[child])
            {
                if (entries[child].IsDirectory && comparer.Equals(entries[child].Name, segment))
                {
                    found = child;
                    break;
                }
            }

            if (found < 0)
            {
                return -1;
            }

            current = found;
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

        var stack = new Stack<(int Record, DirectorySpace Node, string Path)>();
        var roots = FillDirectory(table, links, start, root, directory.FullName, totals, progress);

        foreach (var branch in roots)
        {
            stack.Push(branch);

            while (stack.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();

                var (record, node, path) = stack.Pop();

                foreach (var child in FillDirectory(table, links, record, node, path, totals, progress))
                {
                    stack.Push(child);
                }
            }

            progress?.CompleteTopLevel();
        }

        return root;
    }

    private static List<(int Record, DirectorySpace Node, string Path)> FillDirectory(
        MftTable table,
        MftLinks links,
        int record,
        DirectorySpace node,
        string path,
        MftSubtreeTotals totals,
        ScanProgress? progress)
    {
        progress?.EnterDirectory(path);

        var entries = table.Entries;
        var children = new List<(int, DirectorySpace, string)>();
        var files = 0;

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
                var subDirectory = new DirectorySpace(entry.Name!, node, entry.CreationTime, entry.LastAccessTime);
                node.AddScannedDirectory(subDirectory);
                children.Add((child, subDirectory, Path.Join(path, entry.Name)));
                continue;
            }

            if (!entry.SizeKnown)
            {
                totals.UnknownSizeFiles++;
            }

            node.AddScannedFile(new(entry.Name!, entry.Size, entry.CreationTime, entry.LastAccessTime, FileAttributes.Normal, false));
            files++;
        }

        node.SealFiles();
        progress?.AddFiles(files, node.Size);

        return children;
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
    long DamagedRecords,
    long StaleParents,
    long Rehomed,
    string Report)
{
    public long DroppedObjects => DetachedRecords + NamelessRecords + DamagedRecords;

    public bool IsComplete => DroppedObjects == 0 && UnknownSizeFiles == 0;
}
