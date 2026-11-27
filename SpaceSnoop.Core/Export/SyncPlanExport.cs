using System.Text.Json;

namespace SpaceSnoop.Core.Export;

public sealed record SyncPlanEntry(
    string Path,
    ComparisonEntryKind Kind,
    ComparisonStatus Status,
    SyncAction Action,
    long Bytes,
    string Size);

public sealed record SyncPlanExportModel
{
    public string Tool { get; init; } = "SpaceSnoop";
    public int Schema { get; init; } = 1;
    public string? PlanFreshness { get; init; }
    public string? Version { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public required string LeftPath { get; init; }
    public required string RightPath { get; init; }
    public required ComparisonExportOptions Options { get; init; }
    public required PlannedActions Actions { get; init; }
    public long CopyBytes { get; init; }
    public string CopySize { get; init; } = string.Empty;
    public long DeleteBytes { get; init; }
    public string DeleteSize { get; init; } = string.Empty;
    public IReadOnlyList<SyncPlanEntry> Largest { get; init; } = [];
    public int OmittedEntries { get; init; }
    public ComparisonIncomplete? Incomplete { get; init; }
    public ComparisonSkippedLinks? SkippedLinks { get; init; }
    public bool HasPermanentNetworkActions { get; init; }
}

public static class SyncPlanExport
{
    public const int DefaultEntryLimit = 20;

    public static SyncPlanExportModel Build(
        ComparisonResult result,
        ComparisonExportOptions options,
        string? version = null,
        int entryLimit = DefaultEntryLimit)
    {
        var planned = new List<SyncPlanEntry>();
        Collect(result.Root, planned);

        var actions = result.CountPlannedActions();

        var largest = planned
            .OrderByDescending(static x => x.Bytes)
            .Take(entryLimit)
            .ToList();

        var hasPermanentNetworkActions = HasPermanentNetworkWork(result.LeftPath, result.RightPath, planned);

        return new()
        {
            Version = version,
            GeneratedAt = DateTimeOffset.Now,
            LeftPath = result.LeftPath,
            RightPath = result.RightPath,
            Options = options,
            Actions = actions,
            CopyBytes = actions.CopyBytes,
            CopySize = SizeFormatter.Format(actions.CopyBytes),
            DeleteBytes = actions.DeleteBytes,
            DeleteSize = SizeFormatter.Format(actions.DeleteBytes),
            Largest = largest,
            OmittedEntries = Math.Max(0, planned.Count - largest.Count),
            Incomplete = ComparisonExport.DescribeIncomplete(result),
            SkippedLinks = ComparisonExport.DescribeSkippedLinks(result),
            HasPermanentNetworkActions = hasPermanentNetworkActions,
        };
    }

    public static string ToJson(SyncPlanExportModel model)
    {
        return JsonSerializer.Serialize(model, ExportJson.Options);
    }

    private static bool HasPermanentNetworkWork(string leftPath, string rightPath, List<SyncPlanEntry> planned)
    {
        if (planned.Count == 0)
        {
            return false;
        }

        var leftNoBin = !RecycleBin.SupportsRecycleBin(leftPath);
        var rightNoBin = !RecycleBin.SupportsRecycleBin(rightPath);

        if (!leftNoBin && !rightNoBin)
        {
            return false;
        }

        foreach (var entry in planned)
        {
            if (leftNoBin && (entry.Action == SyncAction.DeleteLeft || (entry.Action == SyncAction.CopyToLeft && entry.Status == ComparisonStatus.Modified)))
            {
                return true;
            }

            if (rightNoBin && (entry.Action == SyncAction.DeleteRight || (entry.Action == SyncAction.CopyToRight && entry.Status == ComparisonStatus.Modified)))
            {
                return true;
            }
        }

        return false;
    }

    private static void Collect(DirectoryComparison dir, List<SyncPlanEntry> entries)
    {
        foreach (var file in dir.Files)
        {
            if (file.Action is SyncAction.None or SyncAction.Skip)
            {
                continue;
            }

            var bytes = file.Action switch
            {
                SyncAction.CopyToRight or SyncAction.DeleteLeft => file.LeftSize ?? 0,
                _ => file.RightSize ?? 0,
            };

            entries.Add(new(file.RelativePath, ComparisonEntryKind.File, file.Status, file.Action, bytes, SizeFormatter.Format(bytes)));
        }

        foreach (var sub in dir.SubDirectories)
        {
            if (sub.Action is not (SyncAction.None or SyncAction.Skip))
            {
                entries.Add(new(sub.RelativePath, ComparisonEntryKind.Directory, sub.Status, sub.Action, 0, SizeFormatter.Format(0)));
            }

            Collect(sub, entries);
        }
    }
}
