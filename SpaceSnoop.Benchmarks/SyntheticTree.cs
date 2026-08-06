using SpaceSnoop.Core.Domain;

namespace SpaceSnoop.Benchmarks;

internal static class SyntheticTree
{
    public const int Seed = 20260805;

    private const int FilesPerDirectory = 12;
    private const int SubDirectoriesPerDirectory = 6;
    private const int MaxDepth = 12;
    private const int ErrorEveryDirectory = 97;
    private const long MaxFileBytes = 1L << 20;
    private const int SpreadSeconds = 1_000_000;

    private static readonly DateTime Stamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);

    private static readonly string[] DirectoryNames = CreateNames("dir", SubDirectoriesPerDirectory);

    private static readonly string[] FileNames = CreateNames("file", FilesPerDirectory);

    public static DirectorySpace BuildScan(IReadOnlyList<FileInfo> samples, int files)
    {
        return BuildScanDirectory(new(samples), "root", null, files, 1);
    }

    public static ComparisonResult BuildComparison(int files)
    {
        var root = BuildComparisonDirectory(new(new(Seed)), string.Empty, string.Empty, files, 1);

        return new(@"L:\left", @"R:\right", root);
    }

    private static DirectorySpace BuildScanDirectory(ScanState state, string name, DirectorySpace? parent, int budget, int depth)
    {
        var directory = new DirectorySpace(name, parent, Stamp, Stamp);
        var take = depth < MaxDepth ? Math.Min(FilesPerDirectory, budget) : budget;

        if (take > 0)
        {
            var batch = new FileInfo[take];

            for (var i = 0; i < take; i++)
            {
                batch[i] = state.NextFile();
            }

            directory.AddFiles(batch.AsSpan());
            budget -= take;
        }

        var children = Math.Min(SubDirectoriesPerDirectory, budget);

        for (var i = 0; i < children; i++)
        {
            directory.Add(BuildScanDirectory(state, DirectoryNames[i], directory, Share(budget, children, i), depth + 1));
        }

        if (depth > 1 && state.Created++ % ErrorEveryDirectory == 0)
        {
            directory.Error();
        }

        return directory;
    }

    private static DirectoryComparison BuildComparisonDirectory(CompareState state, string name, string relative, int budget, int depth)
    {
        var directory = new DirectoryComparison(name, relative);
        var take = depth < MaxDepth ? Math.Min(FilesPerDirectory, budget) : budget;

        for (var i = 0; i < take; i++)
        {
            directory.Files.Add(CreateFile(state, relative, i));
        }

        budget -= take;

        var children = Math.Min(SubDirectoriesPerDirectory, budget);

        for (var i = 0; i < children; i++)
        {
            var childName = DirectoryNames[i];
            var childRelative = relative.Length == 0 ? childName : Path.Combine(relative, childName);
            var child = BuildComparisonDirectory(state, childName, childRelative, Share(budget, children, i), depth + 1);

            child.Status = state.NextDirectoryStatus();
            directory.SubDirectories.Add(child);
        }

        return directory;
    }

    private static int Share(int budget, int children, int index)
    {
        return (budget / children) + (index < budget % children ? 1 : 0);
    }

    private static FileComparison CreateFile(CompareState state, string relative, int index)
    {
        var name = FileNames[index % FileNames.Length];
        var file = new FileComparison(name, relative.Length == 0 ? name : Path.Combine(relative, name));
        var size = 1L + state.Random.NextInt64(MaxFileBytes);
        var left = Stamp.AddSeconds(state.Random.Next(SpreadSeconds));
        var roll = state.Random.Next(100);

        switch (roll)
        {
            case < 70:
                file.Status = ComparisonStatus.Identical;
                file.LeftSize = size;
                file.RightSize = size;
                file.LeftModified = left;
                file.RightModified = left;
                break;

            case < 82:
                file.Status = ComparisonStatus.Modified;
                file.LeftSize = size;
                file.RightSize = size / 2;
                file.LeftModified = left;
                file.RightModified = left.AddMinutes(-30);
                break;

            case < 88:
                file.Status = ComparisonStatus.Modified;
                file.LeftSize = size / 2;
                file.RightSize = size;
                file.LeftModified = left.AddMinutes(-30);
                file.RightModified = left;
                break;

            case < 92:
                file.Status = ComparisonStatus.Modified;
                file.LeftSize = size;
                file.RightSize = size + 1;
                file.LeftModified = left;
                file.RightModified = left;
                break;

            case < 96:
                file.Status = ComparisonStatus.LeftOnly;
                file.LeftSize = size;
                file.LeftModified = left;
                break;

            default:
                file.Status = ComparisonStatus.RightOnly;
                file.RightSize = size;
                file.RightModified = left;
                break;
        }

        return file;
    }

    private static string[] CreateNames(string prefix, int count)
    {
        var names = new string[count];

        for (var i = 0; i < names.Length; i++)
        {
            names[i] = $"{prefix}{i:D2}";
        }

        return names;
    }

    private sealed class ScanState(IReadOnlyList<FileInfo> samples)
    {
        private int _cursor;

        public int Created { get; set; }

        public FileInfo NextFile()
        {
            return samples[_cursor++ % samples.Count];
        }
    }

    private sealed class CompareState(Random random)
    {
        public Random Random { get; } = random;

        public ComparisonStatus NextDirectoryStatus()
        {
            return Random.Next(100) switch
            {
                < 90 => ComparisonStatus.Identical,
                < 95 => ComparisonStatus.LeftOnly,
                _ => ComparisonStatus.RightOnly,
            };
        }
    }
}
