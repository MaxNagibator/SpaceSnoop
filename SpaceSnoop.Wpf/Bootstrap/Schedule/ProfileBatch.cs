using System.IO;

namespace SpaceSnoop.Wpf.Bootstrap.Schedule;

public sealed partial class BatchPairRow(
    string name,
    string left,
    string right,
    bool alreadyExists,
    bool destWillBeCreated)
    : ObservableObject
{
    [ObservableProperty]
    private bool _include = !alreadyExists;

    public string Name { get; } = name;

    public string Left { get; } = left;

    public string Right { get; } = right;

    public bool AlreadyExists { get; } = alreadyExists;

    public bool DestWillBeCreated { get; } = destWillBeCreated;
}

public static class ProfileBatch
{
    public static IReadOnlyList<BatchPairRow> BuildPairs(string sourceParent, string destParent, IReadOnlyList<SyncProfile> existing)
    {
        var rows = new List<BatchPairRow>();

        if (string.IsNullOrWhiteSpace(sourceParent) || string.IsNullOrWhiteSpace(destParent) || !Directory.Exists(sourceParent))
        {
            return rows;
        }

        string[] subdirectories;

        try
        {
            subdirectories = Directory.GetDirectories(sourceParent);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return rows;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in existing)
        {
            seen.Add(Key(profile.Left, profile.Right));
        }

        foreach (var directory in subdirectories)
        {
            var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var right = Path.Combine(destParent, name);

            if (SyncProfile.PathsOverlap(directory, right))
            {
                continue;
            }

            var alreadyExists = !seen.Add(Key(directory, right));
            rows.Add(new(name, directory, right, alreadyExists, !Directory.Exists(right)));
        }

        return rows;
    }

    public static IReadOnlyList<BatchPairRow> Sort(IReadOnlyList<BatchPairRow> rows, int mode)
    {
        IEnumerable<BatchPairRow> ordered = mode switch
        {
            1 => rows.OrderByDescending(row => row.Name, StringComparer.OrdinalIgnoreCase),
            2 => rows.OrderBy(row => row.AlreadyExists).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
        };

        return ordered.ToList();
    }

    private static string Key(string left, string right)
    {
        return $"{Normalize(left)}|{Normalize(right)}";

        static string Normalize(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return path.ToLowerInvariant();
            }
        }
    }
}
