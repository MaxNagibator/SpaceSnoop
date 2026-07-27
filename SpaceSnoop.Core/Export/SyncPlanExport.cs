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

        var copyBytes = planned.Where(static x => IsCopy(x.Action)).Sum(static x => x.Bytes);
        var deleteBytes = planned.Where(static x => !IsCopy(x.Action)).Sum(static x => x.Bytes);

        var largest = planned
            .OrderByDescending(static x => x.Bytes)
            .Take(entryLimit)
            .ToList();

        return new()
        {
            Version = version,
            GeneratedAt = DateTimeOffset.Now,
            LeftPath = result.LeftPath,
            RightPath = result.RightPath,
            Options = options,
            Actions = result.CountPlannedActions(),
            CopyBytes = copyBytes,
            CopySize = SizeFormatter.Format(copyBytes),
            DeleteBytes = deleteBytes,
            DeleteSize = SizeFormatter.Format(deleteBytes),
            Largest = largest,
            OmittedEntries = Math.Max(0, planned.Count - largest.Count),
        };
    }

    public static string ToJson(SyncPlanExportModel model)
    {
        return JsonSerializer.Serialize(model, ExportJson.Options);
    }

    private static bool IsCopy(SyncAction action)
    {
        return action is SyncAction.CopyToLeft or SyncAction.CopyToRight;
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
