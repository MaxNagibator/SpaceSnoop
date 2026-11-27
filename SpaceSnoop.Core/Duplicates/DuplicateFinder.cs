using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Security;

namespace SpaceSnoop.Core.Duplicates;

public sealed class DuplicateFinder(ILogger<DuplicateFinder>? logger = null)
{
    private const int BlockSize = 80 * 1024;
    private const int MinHashBucketCount = 3;
    private const int HashThreshold = 8;
    private const int MaxReportedErrors = 50;
    private const int PrefixHashSize = 4 * 1024;
    private const int FullHashLimit = 64 * 1024;

    private readonly ILogger<DuplicateFinder> _logger = logger ?? NullLogger<DuplicateFinder>.Instance;

    public Task<DuplicateReport> FindAsync(DirectorySpace root, DuplicateOptions options, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        return Task.Run(() => Find(root, options, progress, token), token);
    }

    public DuplicateReport Find(DirectorySpace root, DuplicateOptions options, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        var bySize = new Dictionary<long, SizeBucket>();
        var unreadable = CollectRefs(root, Math.Max(1, options.MinSize), bySize, token);

        var buckets = new List<List<FileRef>>();

        foreach (var entry in bySize.Values)
        {
            if (entry.Rest is not null)
            {
                buckets.Add(entry.Rest);
            }
        }

        var state = new FindState(progress);
        var found = new ConcurrentBag<DuplicateGroup>();

        Parallel.ForEach(
            buckets,
            new()
            {
                MaxDegreeOfParallelism = Math.Max(1, options.MaxParallelism),
                CancellationToken = token,
            },
            x =>
            {
                foreach (var group in Resolve(x, options, state, token))
                {
                    found.Add(group);
                }
            });

        state.ReportFinal();

        var groups = found.OrderByDescending(static x => x.ReclaimableBytes)
            .ThenByDescending(static x => x.Size)
            .ToList();

        var reclaimable = groups.Sum(static x => x.ReclaimableBytes);
        var limit = Math.Max(1, options.GroupLimit);
        var omitted = Math.Max(0, groups.Count - limit);

        if (omitted > 0)
        {
            groups = groups.Take(limit).ToList();
        }

        _logger.DuplicatesFinished(groups.Count, reclaimable, state.Examined);

        return new(groups, reclaimable, state.Examined, omitted, unreadable, state.Errors.ToList());
    }

    internal static int Collect(DirectorySpace root, long minSize, Dictionary<long, List<FileSpace>> bySize, CancellationToken token)
    {
        var refsBySize = new Dictionary<long, SizeBucket>();
        var unreadable = CollectRefs(root, minSize, refsBySize, token);

        foreach (var (size, refs) in refsBySize)
        {
            if (!bySize.TryGetValue(size, out var bucket))
            {
                bucket = new(refs.Rest?.Count ?? 1);
                bySize[size] = bucket;
            }

            if (refs.Rest is null)
            {
                bucket.Add(refs.First.Directory.GetFile(refs.First.Index));
                continue;
            }

            for (var i = 0; i < refs.Rest.Count; i++)
            {
                bucket.Add(refs.Rest[i].Directory.GetFile(refs.Rest[i].Index));
            }
        }

        return unreadable;
    }

    internal static int CollectRefs(DirectorySpace root, long minSize, Dictionary<long, SizeBucket> bySize, CancellationToken token)
    {
        var unreadable = 0;
        var stack = new Stack<DirectorySpace>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();

            var directory = stack.Pop();

            if (directory.State == SpaceState.Error)
            {
                unreadable++;
            }

            var entries = directory.FileEntries;

            for (var i = 0; i < entries.Length; i++)
            {
                var size = entries[i].Size;

                if (size < minSize)
                {
                    continue;
                }

                ref var bucket = ref CollectionsMarshal.GetValueRefOrAddDefault(bySize, size, out var exists);
                var fileRef = new FileRef(directory, i);

                if (!exists)
                {
                    bucket.First = fileRef;
                }
                else if (bucket.Rest is null)
                {
                    bucket.Rest = [bucket.First, fileRef];
                }
                else
                {
                    bucket.Rest.Add(fileRef);
                }
            }

            var subs = directory.SubDirectoriesArray;

            for (var i = 0; i < subs.Length; i++)
            {
                stack.Push(subs[i]);
            }
        }

        return unreadable;
    }

    private static bool IsReadError(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;
    }

    private static FileStream Open(string path)
    {
        return new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BlockSize, FileOptions.SequentialScan);
    }

    private static Probe Examine(FileRef file, string path, bool withHash, FindState state, CancellationToken token)
    {
        var symbolic = (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

        using var stream = Open(path);
        var identity = FileIdentity.Read(stream.SafeFileHandle);
        var hash = withHash ? HashPrefix(stream, state, token) : (UInt128?)null;

        return new(file, path, identity, hash, symbolic);
    }

    private static UInt128 HashPrefix(FileStream stream, FindState state, CancellationToken token)
    {
        return Hash(stream, stream.Length > FullHashLimit ? PrefixHashSize : long.MaxValue, state, token);
    }

    private static UInt128 Hash(FileStream stream, long budget, FindState state, CancellationToken token)
    {
        var hash = new XxHash128();
        var buffer = ArrayPool<byte>.Shared.Rent(BlockSize);

        try
        {
            int read;

            while (budget > 0 && (read = stream.Read(buffer, 0, (int)Math.Min(BlockSize, budget))) > 0)
            {
                token.ThrowIfCancellationRequested();
                hash.Append(buffer.AsSpan(0, read));
                state.AddBytes(read);
                budget -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return hash.GetCurrentHashAsUInt128();
    }

    private static bool ContentEqual(string left, string right, FindState state, CancellationToken token)
    {
        using var first = Open(left);
        using var second = Open(right);

        if (first.Length != second.Length)
        {
            return false;
        }

        var leftBuffer = ArrayPool<byte>.Shared.Rent(BlockSize);
        var rightBuffer = ArrayPool<byte>.Shared.Rent(BlockSize);

        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();

                var read = first.ReadAtLeast(leftBuffer.AsSpan(0, BlockSize), BlockSize, false);
                var other = second.ReadAtLeast(rightBuffer.AsSpan(0, BlockSize), BlockSize, false);

                if (read != other)
                {
                    return false;
                }

                if (read == 0)
                {
                    return true;
                }

                state.AddBytes(read);

                if (!leftBuffer.AsSpan(0, read).SequenceEqual(rightBuffer.AsSpan(0, read)))
                {
                    return false;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(leftBuffer);
            ArrayPool<byte>.Shared.Return(rightBuffer);
        }
    }

    private static DuplicateMemberKind KindOf(bool symbolic, bool first)
    {
        if (symbolic)
        {
            return DuplicateMemberKind.SymbolicLink;
        }

        return first ? DuplicateMemberKind.Copy : DuplicateMemberKind.HardLink;
    }

    private static List<DuplicateGroup> Build(long size, List<List<List<Probe>>> clusters, int memberLimit)
    {
        var groups = new List<DuplicateGroup>();

        foreach (var cluster in clusters)
        {
            var totalMembers = 0;

            for (var i = 0; i < cluster.Count; i++)
            {
                totalMembers += cluster[i].Count;
            }

            if (totalMembers < 2)
            {
                continue;
            }

            var members = new List<DuplicateMember>(totalMembers);
            var distinct = 0;

            foreach (var links in cluster)
            {
                links.Sort(static (left, right) => left.Symbolic.CompareTo(right.Symbolic));

                for (var index = 0; index < links.Count; index++)
                {
                    var kind = KindOf(links[index].Symbolic, index == 0);

                    if (kind == DuplicateMemberKind.Copy)
                    {
                        distinct++;
                    }

                    var probe = links[index];
                    var space = probe.File.Directory.GetFile(probe.File.Index);
                    members.Add(new(space, probe.Path, kind));
                }
            }

            groups.Add(
                members.Count > memberLimit
                    ? new(size, members[..memberLimit], distinct, members[memberLimit..])
                    : new(size, members, distinct));
        }

        return groups;
    }

    private List<DuplicateGroup> Resolve(List<FileRef> bucket, DuplicateOptions options, FindState state, CancellationToken token)
    {
        var fileSize = bucket[0].Directory.FileEntries[bucket[0].Index].Size;
        var withHash = bucket.Count >= MinHashBucketCount;
        var physical = ProbeBucket(bucket, withHash, state, token);

        if (withHash && physical.Count > HashThreshold)
        {
            Refine(physical, fileSize, state, token);
        }

        var clusters = Cluster(physical, withHash, state, token);

        return Build(fileSize, clusters, Math.Max(2, options.MemberLimit));
    }

    private List<List<Probe>> ProbeBucket(List<FileRef> bucket, bool withHash, FindState state, CancellationToken token)
    {
        var physical = new List<List<Probe>>();
        var byIdentity = new Dictionary<FileIdentity, List<Probe>>();

        foreach (var fileRef in bucket)
        {
            token.ThrowIfCancellationRequested();

            var path = Path.Combine(fileRef.Directory.AbsolutePath, fileRef.Directory.GetFileName(fileRef.Index));
            Probe probe;

            try
            {
                probe = Examine(fileRef, path, withHash, state, token);
            }
            catch (Exception exception) when (IsReadError(exception))
            {
                _logger.DuplicateFileSkipped(exception, path);
                state.Fail(path, exception);
                continue;
            }
            finally
            {
                state.Advance(path);
            }

            var identity = probe.Identity;

            if (identity is null)
            {
                physical.Add([probe]);
                continue;
            }

            if (byIdentity.TryGetValue(identity.Value, out var links))
            {
                links.Add(probe);
                continue;
            }

            links = [probe];
            byIdentity[identity.Value] = links;
            physical.Add(links);
        }

        return physical;
    }

    private void Refine(List<List<Probe>> physical, long size, FindState state, CancellationToken token)
    {
        if (size <= FullHashLimit)
        {
            return;
        }

        var crowded = physical.GroupBy(static x => x[0].Digest).Where(static x => x.Count() > HashThreshold);

        foreach (var group in crowded)
        {
            foreach (var links in group)
            {
                token.ThrowIfCancellationRequested();

                var probe = links[0];

                try
                {
                    using var stream = Open(probe.Path);
                    links[0] = probe with
                    {
                        Digest = Hash(stream, long.MaxValue, state, token),
                    };
                }
                catch (Exception exception) when (IsReadError(exception))
                {
                    _logger.DuplicateFileSkipped(exception, probe.Path);
                    state.Fail(probe.Path, exception);
                    links[0] = probe with
                    {
                        Digest = null,
                    };
                }
            }
        }
    }

    private List<List<List<Probe>>> Cluster(List<List<Probe>> physical, bool withHash, FindState state, CancellationToken token)
    {
        var clusters = new List<List<List<Probe>>>();

        if (!withHash || physical.Count < MinHashBucketCount)
        {
            foreach (var links in physical)
            {
                token.ThrowIfCancellationRequested();

                var matched = MatchCluster(clusters, links[0], withHash, state, out var failed, token);

                if (matched is not null)
                {
                    matched.Add(links);
                }
                else if (!failed)
                {
                    clusters.Add([links]);
                }
            }

            return clusters;
        }

        var byDigest = new Dictionary<UInt128, List<List<List<Probe>>>>();
        List<List<List<Probe>>>? unhashed = null;

        foreach (var links in physical)
        {
            token.ThrowIfCancellationRequested();

            var candidate = links[0];

            if (candidate.Digest is { } digest)
            {
                List<List<Probe>>? matched = null;
                var matchFailed = false;

                if (byDigest.TryGetValue(digest, out var digestClusters))
                {
                    matched = MatchCluster(digestClusters, candidate, false, state, out matchFailed, token);
                }

                if (matched is null && !matchFailed && unhashed is not null)
                {
                    matched = MatchCluster(unhashed, candidate, false, state, out matchFailed, token);
                }

                if (matched is not null)
                {
                    matched.Add(links);
                }
                else if (!matchFailed)
                {
                    List<List<Probe>> created = [links];
                    clusters.Add(created);

                    if (digestClusters is null)
                    {
                        digestClusters = [];
                        byDigest[digest] = digestClusters;
                    }

                    digestClusters.Add(created);
                }
            }
            else
            {
                var matched = MatchCluster(clusters, candidate, false, state, out var failed, token);

                if (matched is not null)
                {
                    matched.Add(links);
                }
                else if (!failed)
                {
                    List<List<Probe>> created = [links];
                    clusters.Add(created);
                    (unhashed ??= []).Add(created);
                }
            }
        }

        return clusters;
    }

    private List<List<Probe>>? MatchCluster(
        List<List<List<Probe>>> clusters,
        Probe candidate,
        bool withHash,
        FindState state,
        out bool failed,
        CancellationToken token)
    {
        failed = false;

        foreach (var cluster in clusters)
        {
            var other = cluster[0][0];

            if (withHash && candidate.Digest is { } digest && other.Digest is { } head && digest != head)
            {
                continue;
            }

            try
            {
                if (!ContentEqual(other.Path, candidate.Path, state, token))
                {
                    continue;
                }
            }
            catch (Exception exception) when (IsReadError(exception))
            {
                _logger.DuplicateFileSkipped(exception, candidate.Path);
                state.Fail(candidate.Path, exception);
                failed = true;

                return null;
            }

            return cluster;
        }

        return null;
    }

    internal readonly record struct FileRef(DirectorySpace Directory, int Index);

    internal struct SizeBucket
    {
        public FileRef First;
        public List<FileRef>? Rest;
    }

    private readonly record struct Probe(FileRef File, string Path, FileIdentity? Identity, UInt128? Digest, bool Symbolic);

    private sealed class FindState(IProgress<OperationProgress>? progress)
    {
        private const long ReportIntervalMs = 50;

        private int _examined;
        private long _bytes;
        private long _nextReportTick;
        private int _lastReportedExamined;
        private long _lastReportedBytes = -1;
        private string? _lastPath;

        public ConcurrentQueue<string> Errors { get; } = new();

        public int Examined => Volatile.Read(ref _examined);

        public void AddBytes(long value)
        {
            Interlocked.Add(ref _bytes, value);
        }

        public void Advance(string path)
        {
            Volatile.Write(ref _lastPath, path);
            var completed = Interlocked.Increment(ref _examined);

            if (progress is null)
            {
                return;
            }

            var now = Environment.TickCount64;
            var next = Volatile.Read(ref _nextReportTick);

            if (now >= next && Interlocked.CompareExchange(ref _nextReportTick, now + ReportIntervalMs, next) == next)
            {
                var bytes = Interlocked.Read(ref _bytes);
                Volatile.Write(ref _lastReportedExamined, completed);
                Volatile.Write(ref _lastReportedBytes, bytes);
                progress.Report(new(completed, path, bytes));
            }
        }

        public void ReportFinal()
        {
            if (progress is null)
            {
                return;
            }

            var examined = Volatile.Read(ref _examined);
            var bytes = Interlocked.Read(ref _bytes);

            if (examined > 0
                && (Volatile.Read(ref _lastReportedExamined) != examined
                    || Volatile.Read(ref _lastReportedBytes) != bytes))
            {
                Volatile.Write(ref _lastReportedExamined, examined);
                Volatile.Write(ref _lastReportedBytes, bytes);
                progress.Report(new(examined, _lastPath ?? string.Empty, bytes));
            }
        }

        public void Fail(string path, Exception exception)
        {
            if (Errors.Count < MaxReportedErrors)
            {
                Errors.Enqueue($"«{path}»: {exception.Message}");
            }
        }
    }
}
