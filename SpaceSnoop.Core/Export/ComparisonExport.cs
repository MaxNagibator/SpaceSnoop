using SpaceSnoop.Core.Git;
using System.Text.Json;

namespace SpaceSnoop.Core.Export;

public enum ComparisonEntryKind
{
    None = 0,
    File = 1,
    Directory = 2,
}

public sealed record ComparisonExportOptions(SyncMode Mode, SyncWinner Winner, bool Mirror, string Exclusions);

public sealed record ComparisonExportEntry
{
    public required string Path { get; init; }
    public required ComparisonEntryKind Kind { get; init; }
    public required ComparisonStatus Status { get; init; }
    public required SyncAction Action { get; init; }
    public long? LeftSize { get; init; }
    public long? RightSize { get; init; }
    public DateTime? LeftModified { get; init; }
    public DateTime? RightModified { get; init; }
    public FileTypeConflict TypeConflict { get; init; }
    public bool? SizeDiffers { get; init; }
    public double? TimeDeltaSeconds { get; init; }
    public string? LeftHash { get; init; }
    public string? RightHash { get; init; }
}

public sealed record ComparisonExportDirectory(string Path, int Differing, int LeftOnly, int RightOnly, int Modified, long DifferingBytes);

public sealed record ComparisonExportTotals(
    IReadOnlyDictionary<string, int> Files,
    IReadOnlyDictionary<string, int> Directories,
    PlannedActions Planned);

public sealed record ComparisonIncomplete(int Count, IReadOnlyList<string> Paths, int OmittedPaths);

public sealed record ComparisonSkippedLinks(int Count, IReadOnlyList<string> Paths, int OmittedPaths);

public sealed record ComparisonExportGit(GitRepoState? Left, GitRepoState? Right, string Verdict);

public sealed record ComparisonExportSync(
    int Copied,
    int Deleted,
    IReadOnlyList<SyncError> Errors,
    IReadOnlyList<SyncMismatch> Mismatches);

public sealed record ComparisonExportModel
{
    public string Tool { get; init; } = "SpaceSnoop";
    public int Schema { get; init; } = 1;
    public string? Version { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public required string LeftPath { get; init; }
    public required string RightPath { get; init; }
    public required ComparisonExportOptions Options { get; init; }
    public required ComparisonExportTotals Totals { get; init; }
    public IReadOnlyList<ComparisonExportDirectory> Directories { get; init; } = [];
    public IReadOnlyList<ComparisonExportEntry> Entries { get; init; } = [];
    public int OmittedEntries { get; init; }
    public ComparisonIncomplete? Incomplete { get; init; }
    public ComparisonSkippedLinks? SkippedLinks { get; init; }
    public ComparisonExportGit? Git { get; init; }
    public ComparisonExportSync? LastSync { get; init; }
}

public static class ComparisonExport
{
    public const int DefaultEntryLimit = 2000;

    public const int IncompletePathLimit = 20;

    private const string RootDirectoryName = ".";

    public static ComparisonExportModel Build(
        ComparisonResult result,
        ComparisonExportOptions options,
        string? version = null,
        int entryLimit = DefaultEntryLimit)
    {
        var collected = new List<ComparisonExportEntry>();
        Collect(result.Root, collected);

        var omitted = Math.Max(0, collected.Count - entryLimit);

        var entries = collected
            .OrderByDescending(Weight)
            .Take(entryLimit)
            .OrderBy(static x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new()
        {
            Version = version,
            GeneratedAt = DateTimeOffset.Now,
            LeftPath = result.LeftPath,
            RightPath = result.RightPath,
            Options = options,
            Totals = new(
                result.GetStatistics().ToDictionary(static x => x.Key.ToString(), static x => x.Value),
                result.GetDirectoryStatistics().ToDictionary(static x => x.Key.ToString(), static x => x.Value),
                result.CountPlannedActions()),
            Directories = BuildDirectories(result.Root),
            Entries = entries,
            OmittedEntries = omitted,
            Incomplete = DescribeIncomplete(result),
            SkippedLinks = DescribeSkippedLinks(result),
        };
    }

    public static ComparisonIncomplete? DescribeIncomplete(ComparisonResult result, int pathLimit = IncompletePathLimit)
    {
        var paths = result.IncompleteDirectories();

        if (paths.Count == 0)
        {
            return null;
        }

        var shown = paths.Take(pathLimit).ToList();

        return new(paths.Count, shown, paths.Count - shown.Count);
    }

    public static ComparisonSkippedLinks? DescribeSkippedLinks(ComparisonResult result, int pathLimit = IncompletePathLimit)
    {
        var paths = result.SkippedLinks();

        if (paths.Count == 0)
        {
            return null;
        }

        var shown = paths.Take(pathLimit).ToList();

        return new(paths.Count, shown, paths.Count - shown.Count);
    }

    public static string ToJson(ComparisonExportModel model)
    {
        return JsonSerializer.Serialize(model, ExportJson.Options);
    }

    private static long Weight(ComparisonExportEntry entry)
    {
        return Math.Max(entry.LeftSize ?? 0, entry.RightSize ?? 0);
    }

    private static void Collect(DirectoryComparison dir, List<ComparisonExportEntry> entries)
    {
        foreach (var file in dir.Files)
        {
            if (file.Status == ComparisonStatus.Identical)
            {
                continue;
            }

            entries.Add(FromFile(file));
        }

        foreach (var sub in dir.SubDirectories)
        {
            if (sub.Status is ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly)
            {
                entries.Add(FromDirectory(sub));
            }

            Collect(sub, entries);
        }
    }

    private static ComparisonExportEntry FromFile(FileComparison file)
    {
        var compared = file.TypeConflict == FileTypeConflict.None
                       && file.Status is ComparisonStatus.Modified or ComparisonStatus.Conflict;

        double? delta = compared && file is { LeftModified: { } left, RightModified: { } right }
            ? Math.Round((left - right).TotalSeconds, 1)
            : null;

        return new()
        {
            Path = file.RelativePath,
            Kind = ComparisonEntryKind.File,
            Status = file.Status,
            Action = file.Action,
            TypeConflict = file.TypeConflict,
            LeftSize = file.LeftSize,
            RightSize = file.RightSize,
            LeftModified = file.LeftModified,
            RightModified = file.RightModified,
            SizeDiffers = compared ? file.LeftSize != file.RightSize : null,
            TimeDeltaSeconds = delta,
            LeftHash = file.LeftHash,
            RightHash = file.RightHash,
        };
    }

    private static ComparisonExportEntry FromDirectory(DirectoryComparison dir)
    {
        return new()
        {
            Path = dir.RelativePath,
            Kind = ComparisonEntryKind.Directory,
            Status = dir.Status,
            Action = dir.Action,
            LeftModified = dir.LeftModified,
            RightModified = dir.RightModified,
        };
    }

    private static List<ComparisonExportDirectory> BuildDirectories(DirectoryComparison root)
    {
        var directories = new List<ComparisonExportDirectory>();
        var rootFiles = Summarize(RootDirectoryName, root, false);

        if (rootFiles.Differing > 0)
        {
            directories.Add(rootFiles);
        }

        foreach (var sub in root.SubDirectories)
        {
            var summary = Summarize(sub.Name, sub, true);

            if (summary.Differing > 0)
            {
                directories.Add(summary);
            }
        }

        return directories
            .OrderByDescending(static x => x.DifferingBytes)
            .ThenByDescending(static x => x.Differing)
            .ToList();
    }

    private static ComparisonExportDirectory Summarize(string path, DirectoryComparison dir, bool recurse)
    {
        var differing = 0;
        var leftOnly = 0;
        var rightOnly = 0;
        var modified = 0;
        var bytes = 0L;

        Walk(dir);
        return new(path, differing, leftOnly, rightOnly, modified, bytes);

        void Walk(DirectoryComparison current)
        {
            foreach (var file in current.Files)
            {
                switch (file.Status)
                {
                    case ComparisonStatus.Identical:
                        continue;

                    case ComparisonStatus.LeftOnly:
                        leftOnly++;
                        break;

                    case ComparisonStatus.RightOnly:
                        rightOnly++;
                        break;

                    default:
                        modified++;
                        break;
                }

                differing++;
                bytes += Math.Max(file.LeftSize ?? 0, file.RightSize ?? 0);
            }

            if (!recurse)
            {
                return;
            }

            foreach (var sub in current.SubDirectories)
            {
                Walk(sub);
            }
        }
    }
}
