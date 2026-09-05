using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Hashing;
using System.Security;

namespace SpaceSnoop.Core.Duplicates;

public sealed class DuplicateFinder(ILogger<DuplicateFinder>? logger = null)
{
    private const int BlockSize = 80 * 1024;
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
        var bySize = new Dictionary<long, List<FileSpace>>();
        var unreadable = Collect(root, Math.Max(1, options.MinSize), bySize, token);

        var buckets = bySize.Values.Where(static x => x.Count > 1).ToList();
        var state = new FindState(progress);
        var found = new ConcurrentBag<DuplicateGroup>();

        Parallel.ForEach(
            buckets,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.MaxParallelism), CancellationToken = token },
            x =>
            {
                foreach (var group in Resolve(x, options, state, token))
                {
                    found.Add(group);
                }
            });

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

    private static int Collect(DirectorySpace root, long minSize, Dictionary<long, List<FileSpace>> bySize, CancellationToken token)
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

            foreach (var file in directory.Files)
            {
                if (file.Size < minSize)
                {
                    continue;
                }

                if (!bySize.TryGetValue(file.Size, out var bucket))
                {
                    bucket = [];
                    bySize[file.Size] = bucket;
                }

                bucket.Add(file);
            }

            foreach (var sub in directory.SubDirectories)
            {
                stack.Push(sub);
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

    private static Probe Examine(FileSpace space, string path, bool withHash, FindState state, CancellationToken token)
    {
        var symbolic = (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

        using var stream = Open(path);
        var identity = FileIdentity.Read(stream.SafeFileHandle);
        var hash = withHash ? HashPrefix(stream, state, token) : (UInt128?)null;

        return new(space, path, identity, hash, symbolic);
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
            var members = new List<DuplicateMember>();
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

                    members.Add(new(links[index].Space, links[index].Path, kind));
                }
            }

            if (members.Count < 2)
            {
                continue;
            }

            var omitted = Math.Max(0, members.Count - memberLimit);

            groups.Add(new(size, omitted > 0 ? members.Take(memberLimit).ToList() : members, distinct, omitted));
        }

        return groups;
    }

    private List<DuplicateGroup> Resolve(List<FileSpace> bucket, DuplicateOptions options, FindState state, CancellationToken token)
    {
        var withHash = bucket.Count > HashThreshold;
        var physical = ProbeBucket(bucket, withHash, state, token);

        if (withHash)
        {
            Refine(physical, bucket[0].Size, state, token);
        }

        var clusters = Cluster(physical, withHash, state, token);

        return Build(bucket[0].Size, clusters, Math.Max(2, options.MemberLimit));
    }

    private List<List<Probe>> ProbeBucket(List<FileSpace> bucket, bool withHash, FindState state, CancellationToken token)
    {
        var physical = new List<List<Probe>>();
        var byIdentity = new Dictionary<FileIdentity, List<Probe>>();

        foreach (var space in bucket)
        {
            token.ThrowIfCancellationRequested();

            var path = space.AbsolutePath;
            Probe probe;

            try
            {
                probe = Examine(space, path, withHash, state, token);
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
                    links[0] = probe with { Digest = Hash(stream, long.MaxValue, state, token) };
                }
                catch (Exception exception) when (IsReadError(exception))
                {
                    _logger.DuplicateFileSkipped(exception, probe.Path);
                    state.Fail(probe.Path, exception);
                    links[0] = probe with { Digest = null };
                }
            }
        }
    }

    private List<List<List<Probe>>> Cluster(List<List<Probe>> physical, bool withHash, FindState state, CancellationToken token)
    {
        var clusters = new List<List<List<Probe>>>();

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

    private readonly record struct Probe(FileSpace Space, string Path, FileIdentity? Identity, UInt128? Digest, bool Symbolic);

    private sealed class FindState(IProgress<OperationProgress>? progress)
    {
        private int _examined;
        private long _bytes;

        public ConcurrentQueue<string> Errors { get; } = new();

        public int Examined => Volatile.Read(ref _examined);

        public void AddBytes(long value)
        {
            Interlocked.Add(ref _bytes, value);
        }

        public void Advance(string path)
        {
            var completed = Interlocked.Increment(ref _examined);
            progress?.Report(new(completed, path, Interlocked.Read(ref _bytes)));
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
