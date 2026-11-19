using SpaceSnoop.Core;
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
    private const string RootName = "root";
    private const int MarkedDirectoryEvery = 8;
    private const int ServiceDepth = 3;
    private const double SizeExponentBits = 30;
    private const int RepeatedSizePercent = 10;

    private static readonly DateTime Stamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);

    private static readonly string[] DirectoryNames = CreateNames("dir", SubDirectoriesPerDirectory);

    private static readonly string[] FileNames = CreateNames("file", FilesPerDirectory);

    private static readonly string[] ServiceDirectoryNames = [".git", "bin", "obj"];

    public static DirectorySpace BuildScan(IReadOnlyList<FileInfo> samples, int files)
    {
        return BuildScanDirectory(new SampleScanState(samples), RootName, CreateScanParent(), files, 1);
    }

    public static DirectorySpace BuildSizedScan(int files)
    {
        return BuildScanDirectory(new SizedScanState(new(Seed)), RootName, CreateScanParent(), files, 1);
    }

    public static DirectorySpace BuildFlatScan(IReadOnlyList<FileInfo> samples, int files)
    {
        var directory = new DirectorySpace(RootName, CreateScanParent(), Stamp, Stamp);
        new SampleScanState(samples).AddFiles(directory, files);

        return directory;
    }

    public static List<SpaceBase> CollectFiles(DirectorySpace root)
    {
        var files = new List<SpaceBase>();
        var pending = new Stack<DirectorySpace>();
        pending.Push(root);

        while (pending.TryPop(out var directory))
        {
            files.AddRange(directory.Files);

            for (var i = directory.SubDirectories.Count - 1; i >= 0; i--)
            {
                pending.Push(directory.SubDirectories[i]);
            }
        }

        return files;
    }

    public static List<SpaceBase> MarkEvenly(DirectorySpace root, int count)
    {
        var marked = new List<SpaceBase>(count);

        MarkSpread(CollectLeafDirectories(root), count / MarkedDirectoryEvery, marked);

        var files = CollectFiles(root).Where(static x => !x.IsDeleted).ToList();
        MarkSpread(files, count - marked.Count, marked);

        return marked;
    }

    public static ComparisonResult BuildComparison(int files)
    {
        var root = BuildComparisonDirectory(new(new(Seed)), string.Empty, string.Empty, files, 1);

        return new(@"L:\left", @"R:\right", root);
    }

    private static DirectorySpace CreateScanParent()
    {
        return new(@"C:\SpaceSnoopBench", null, Stamp, Stamp);
    }

    private static List<SpaceBase> CollectLeafDirectories(DirectorySpace root)
    {
        var leaves = new List<SpaceBase>();
        var pending = new Stack<DirectorySpace>();
        pending.Push(root);

        while (pending.TryPop(out var directory))
        {
            if (directory.SubDirectories.Count == 0 && directory != root && directory.State != SpaceState.Error)
            {
                leaves.Add(directory);
            }

            for (var i = directory.SubDirectories.Count - 1; i >= 0; i--)
            {
                pending.Push(directory.SubDirectories[i]);
            }
        }

        return leaves;
    }

    private static void MarkSpread(List<SpaceBase> candidates, int count, List<SpaceBase> marked)
    {
        var take = Math.Min(count, candidates.Count);
        var step = candidates.Count / Math.Max(take, 1);

        for (var i = 0; i < take; i++)
        {
            var item = candidates[i * step];
            item.Delete();
            marked.Add(item);
        }
    }

    private static DirectorySpace BuildScanDirectory(ScanState state, string name, DirectorySpace? parent, int budget, int depth)
    {
        var directory = new DirectorySpace(name, parent, Stamp, Stamp);
        var take = depth < MaxDepth ? Math.Min(FilesPerDirectory, budget) : budget;

        if (take > 0)
        {
            state.AddFiles(directory, take);
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
            var childName = depth + 1 == ServiceDepth && i == children - 1 ? state.NextServiceName() : DirectoryNames[i];
            var childRelative = relative.Length == 0 ? childName : Path.Combine(relative, childName);
            var child = BuildComparisonDirectory(state, childName, childRelative, Share(budget, children, i), depth + 1);

            child.Status = state.NextDirectoryStatus();
            directory.SubDirectories.Add(child);
        }

        return directory;
    }

    private static int Share(int budget, int children, int index)
    {
        return (budget / children) + (index < (budget % children) ? 1 : 0);
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

    private abstract class ScanState
    {
        public abstract void AddFiles(DirectorySpace directory, int count);
        public int Created { get; set; }
    }

    private sealed class SampleScanState(IReadOnlyList<FileInfo> samples) : ScanState
    {
        private int _cursor;

        public override void AddFiles(DirectorySpace directory, int count)
        {
            var batch = new FileInfo[count];

            for (var i = 0; i < count; i++)
            {
                batch[i] = samples[_cursor++ % samples.Count];
            }

            directory.AddFiles(batch.AsSpan());
        }
    }

    private sealed class SizedScanState(Random random) : ScanState
    {
        private readonly List<long> _sizes = [];

        public override void AddFiles(DirectorySpace directory, int count)
        {
            var files = new List<FileSpace>(count);

            for (var i = 0; i < count; i++)
            {
                var entry = new ScanEntry(FileNames[i % FileNames.Length], NextSize(), Stamp, Stamp, FileAttributes.Normal, false);
                files.Add(FileSpace.Create(entry, directory));
            }

            directory.SetScanned(files, []);
        }

        private long NextSize()
        {
            var size = _sizes.Count > 0 && random.Next(100) < RepeatedSizePercent
                ? _sizes[random.Next(_sizes.Count)]
                : (long)Math.Pow(2, random.NextDouble() * SizeExponentBits);

            _sizes.Add(size);

            return size;
        }
    }

    private sealed class CompareState(Random random)
    {
        private int _services;

        public Random Random { get; } = random;

        public string NextServiceName()
        {
            return ServiceDirectoryNames[_services++ % ServiceDirectoryNames.Length];
        }

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
