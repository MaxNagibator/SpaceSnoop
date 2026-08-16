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

    public MftScanResult Calculate(DirectoryInfo directory, ScanProgress? progress, CancellationToken cancel = default)
    {
        var letter = MftReader.VolumeLetter(directory.FullName)
                     ?? throw new InvalidOperationException($"Путь {directory.FullName} не лежит на локальном томе с буквой");

        var table = MftReader.Read(letter, cancel);
        var links = MftLinks.Build(table, cancel);
        var start = Locate(table, links, directory, letter);

        if (start < 0)
        {
            throw new DirectoryNotFoundException($"Каталог {directory.FullName} не найден в $MFT тома {letter}:");
        }

        var root = Materialize(table, links, start, directory, progress, cancel);
        root.AggregateTotals();
        root.FixAbsolutePath(directory);

        var statistics = table.Statistics;
        _log.MftScanCompleted(directory.FullName, table.Entries.Length, statistics.ExtraNames, statistics.ExtraNameBytes);

        return new(root,
            statistics.ExtraNames,
            statistics.ExtraNameBytes,
            statistics.SkippedLinks,
            statistics.OrphanFiles,
            $"записей {statistics.RecordsScanned:N0}, занято {statistics.RecordsInUse:N0}, расширений {statistics.Extensions:N0}, " +
            $"безымянных {statistics.Nameless:N0}, оторванных {statistics.Detached:N0}");
    }

    private static int Locate(MftTable table, MftLinks links, DirectoryInfo directory, char letter)
    {
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
                if (table.Entries[child].IsDirectory && comparer.Equals(table.Entries[child].Name, segment))
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
        var roots = FillDirectory(table, links, start, root, directory.FullName, progress);

        foreach (var branch in roots)
        {
            stack.Push(branch);

            while (stack.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();

                var (record, node, path) = stack.Pop();

                foreach (var child in FillDirectory(table, links, record, node, path, progress))
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
        ScanProgress? progress)
    {
        progress?.EnterDirectory(path);

        var entries = table.Entries;
        var children = new List<(int, DirectorySpace, string)>();
        var files = 0;

        for (var child = links.FirstChild[record]; child >= 0; child = links.NextSibling[child])
        {
            ref var entry = ref entries[child];

            if (entry.IsDirectory)
            {
                var subDirectory = new DirectorySpace(entry.Name!, node, entry.CreationTime, entry.LastAccessTime);
                node.AddScannedDirectory(subDirectory);
                children.Add((child, subDirectory, Path.Join(path, entry.Name)));
                continue;
            }

            node.AddScannedFile(new(entry.Name!, entry.Size, entry.CreationTime, entry.LastAccessTime, FileAttributes.Normal, false));
            files++;
        }

        node.SealFiles();
        progress?.AddFiles(files, node.Size);

        return children;
    }
}

public sealed record MftScanResult(
    DirectorySpace Root,
    long ExtraNames,
    long ExtraNameBytes,
    long SkippedLinks,
    long OrphanFiles,
    string Report);
