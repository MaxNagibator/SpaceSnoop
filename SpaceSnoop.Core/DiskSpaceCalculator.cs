using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Domain;
using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;

namespace SpaceSnoop.Core;

/// <summary>
/// Калькулятор для вычисления занимаемого дискового пространства директории и ее подкаталогов.
/// </summary>
public class DiskSpaceCalculator(ILogger<DiskSpaceCalculator>? logger = null)
{
    private static readonly EnumerationOptions Enumeration = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    private readonly ILogger _log = logger ?? NullLogger<DiskSpaceCalculator>.Instance;

    /// <summary>
    /// Вычисляет занимаемое дисковое пространство указанной директории и ее подкаталогов.
    /// </summary>
    /// <param name="directory">Директория, для которой нужно вычислить занимаемое дисковое пространство.</param>
    /// <param name="cancel">Токен отмены операции.</param>
    /// <returns>Объект <see cref="DirectorySpace" /> с вычисленной информацией о занимаемом дисковом пространстве.</returns>
    public DirectorySpace Calculate(DirectoryInfo directory, CancellationToken cancel = default)
    {
        return Calculate(directory, null, cancel);
    }

    /// <summary>
    /// Вычисляет занимаемое дисковое пространство указанной директории и ее подкаталогов,
    /// сообщая ход выполнения через <paramref name="progress" />.
    /// </summary>
    /// <param name="directory">Директория, для которой нужно вычислить занимаемое дисковое пространство.</param>
    /// <param name="progress">Приёмник прогресса сканирования (может быть <c>null</c>).</param>
    /// <param name="cancel">Токен отмены операции.</param>
    /// <returns>Объект <see cref="DirectorySpace" /> с вычисленной информацией о занимаемом дисковом пространстве.</returns>
    /// <exception cref="ScanRootLinkException">Корень – ссылка на каталог (junction, symlink): её цель не обходится.</exception>
    public DirectorySpace Calculate(DirectoryInfo directory, ScanProgress? progress, CancellationToken cancel = default)
    {
        ThrowIfLink(directory);

        var store = new ScanTreeStore(1, singleWriter: true);
        var writer = new FileSlabWriter(store);
        var root = CreateRoot(directory, store);
        var children = new List<ScanChild>();
        var files = new List<FileEntry>();
        var skips = new SkipJournal();

        ReadDirectory(ToEnumerationPath(directory.FullName), root, store, writer, default, progress, skips, cancel, children, files);
        progress?.SetTopLevelTotal(children.Count);

        foreach (var child in children)
        {
            ScanRecursive(child, store, writer, files, progress, skips, cancel);
            progress?.CompleteTopLevel();
        }

        store.Freeze();
        root.AggregateTotals();
        root.FixAbsolutePath(directory);
        return root;
    }

    /// <summary>
    /// Вычисляет занимаемое дисковое пространство указанной директории и ее подкаталогов в многопоточном режиме,
    /// используя в качестве степени параллелизма число логических процессоров.
    /// </summary>
    /// <param name="directory">Директория, для которой нужно вычислить занимаемое дисковое пространство.</param>
    /// <param name="cancel">Токен отмены операции.</param>
    /// <returns>Объект <see cref="DirectorySpace" /> с вычисленной информацией о занимаемом дисковом пространстве.</returns>
    /// <remarks>Повышенное выделение памяти</remarks>
    public DirectorySpace CalculateMultithreaded(DirectoryInfo directory, CancellationToken cancel = default)
    {
        return CalculateMultithreaded(directory, Environment.ProcessorCount, null, cancel);
    }

    /// <summary>
    /// Вычисляет занимаемое дисковое пространство указанной директории и ее подкаталогов в многопоточном режиме
    /// с заданной степенью параллелизма.
    /// </summary>
    /// <param name="directory">Директория, для которой нужно вычислить занимаемое дисковое пространство.</param>
    /// <param name="maxDegreeOfParallelism">
    /// Максимальное число каталогов, обходимых одновременно. Значения меньше 1 поднимаются до 1.
    /// </param>
    /// <param name="cancel">Токен отмены операции.</param>
    /// <returns>Объект <see cref="DirectorySpace" /> с вычисленной информацией о занимаемом дисковом пространстве.</returns>
    /// <remarks>Повышенное выделение памяти; растёт со степенью параллелизма.</remarks>
    public DirectorySpace CalculateMultithreaded(DirectoryInfo directory, int maxDegreeOfParallelism, CancellationToken cancel = default)
    {
        return CalculateMultithreaded(directory, maxDegreeOfParallelism, null, cancel);
    }

    /// <summary>
    /// Вычисляет занимаемое дисковое пространство в многопоточном режиме с заданной степенью
    /// параллелизма, сообщая ход выполнения через <paramref name="progress" />.
    /// </summary>
    /// <param name="directory">Директория, для которой нужно вычислить занимаемое дисковое пространство.</param>
    /// <param name="maxDegreeOfParallelism">
    /// Максимальное число каталогов, обходимых одновременно. Значения меньше 1 поднимаются до 1.
    /// </param>
    /// <param name="progress">Приёмник прогресса сканирования (может быть <c>null</c>).</param>
    /// <param name="cancel">Токен отмены операции.</param>
    /// <returns>Объект <see cref="DirectorySpace" /> с вычисленной информацией о занимаемом дисковом пространстве.</returns>
    /// <remarks>Повышенное выделение памяти; растёт со степенью параллелизма.</remarks>
    /// <exception cref="ScanRootLinkException">Корень – ссылка на каталог (junction, symlink): её цель не обходится.</exception>
    public DirectorySpace CalculateMultithreaded(DirectoryInfo directory, int maxDegreeOfParallelism, ScanProgress? progress, CancellationToken cancel = default)
    {
        ThrowIfLink(directory);

        var store = new ScanTreeStore(64);
        var root = CreateRoot(directory, store);

        ScanParallel(ToEnumerationPath(directory.FullName), root, store, Math.Max(1, maxDegreeOfParallelism), progress, cancel);

        store.Freeze();
        root.AggregateTotals();
        root.FixAbsolutePath(directory);
        return root;
    }

    /// <summary>
    /// Объясняет, почему каталог нельзя взять корнем скана: он ссылка на другой каталог (junction, symlink), которую обход
    /// пропустил бы внутри дерева, либо точка повторного разбора, вид которой не удалось прочитать.
    /// Том, смонтированный в папку, ссылкой не считается: корнем скана он допустим. Недоступный для чтения атрибутов путь
    /// ссылкой не считается.
    /// </summary>
    /// <param name="directory">Проверяемый каталог.</param>
    /// <returns>Текст отказа для человека или <c>null</c>, если каталог можно сканировать.</returns>
    public static string? DescribeRootRefusal(DirectoryInfo directory)
    {
        var path = Path.TrimEndingDirectorySeparator(directory.FullName);

        if (UncPath.IsShareRoot(path))
        {
            return null;
        }

        if (!IsReparsePoint(directory, out _))
        {
            return null;
        }

        return ReparsePoint.IsLink(path) switch
        {
            null => ScanRootLinkException.DescribeUnverified(path),
            true when !ReparsePoint.IsMountedVolume(path) => ScanRootLinkException.Describe(path),
            _ => null,
        };
    }

    /// <summary>
    /// Проверяет, что каталог – точка повторного разбора любого вида, включая том, смонтированный в папку.
    /// </summary>
    /// <param name="directory">Проверяемый каталог.</param>
    /// <returns><c>true</c>, если у каталога есть атрибут <see cref="FileAttributes.ReparsePoint" />.</returns>
    public static bool IsReparsePoint(DirectoryInfo directory)
    {
        return IsReparsePoint(directory, out _);
    }

    private static string ToEnumerationPath(string path)
    {
        if (!Path.IsPathFullyQualified(path)
            || path.Contains('/')
            || path.StartsWith(@"\\?\", StringComparison.Ordinal)
            || path.StartsWith(@"\\.\", StringComparison.Ordinal)
            || path.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            return path;
        }

        return path.StartsWith(@"\\", StringComparison.Ordinal)
            ? string.Concat(@"\\?\UNC\", path.AsSpan(2))
            : string.Concat(@"\\?\", path);
    }

    internal static string FromEnumerationPath(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
        {
            return string.Concat(@"\\", path.AsSpan(8));
        }

        return path.StartsWith(@"\\?\", StringComparison.Ordinal) && Path.IsPathFullyQualified(path.AsSpan(4))
            ? path[4..]
            : path;
    }

    private static bool IsReparsePoint(DirectoryInfo directory, out string path)
    {
        path = Path.TrimEndingDirectorySeparator(directory.FullName);

        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (IsTraversalError(exception))
        {
            return false;
        }
    }

    private void ThrowIfLink(DirectoryInfo directory)
    {
        var refusal = DescribeRootRefusal(directory);

        if (refusal is null)
        {
            return;
        }

        _log.ScanReparsePointSkipped(directory.FullName);
        throw new ScanRootLinkException(directory.FullName, refusal);
    }

    private static DirectorySpace CreateRoot(DirectoryInfo directory, ScanTreeStore store)
    {
        return new(
            store,
            store.Names.Intern(directory.Name),
            null,
            TimestampEncoding.FromDateTime(directory.CreationTime),
            TimestampEncoding.FromDateTime(directory.LastAccessTime),
            0);
    }

    private static bool IsTraversalError(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;
    }

    private void ScanRecursive(
        ScanChild item,
        ScanTreeStore store,
        FileSlabWriter writer,
        List<FileEntry> files,
        ScanProgress? progress,
        SkipJournal skips,
        CancellationToken cancel)
    {
        var children = new List<ScanChild>();

        ReadDirectory(item.Path, item.Node, store, writer, default, progress, skips, cancel, children, files);

        foreach (var child in children)
        {
            ScanRecursive(child, store, writer, files, progress, skips, cancel);
        }
    }

    private void ScanParallel(string rootPath, DirectorySpace root, ScanTreeStore store, int degree, ScanProgress? progress, CancellationToken cancel)
    {
        using var run = new ScanRun(cancel);
        run.Queue.Add(new(rootPath, root), cancel);

        var threads = new Thread?[degree];

        try
        {
            for (var index = 0; index < degree; index++)
            {
                var thread = new Thread(() => Work(run, store, progress, cancel))
                {
                    IsBackground = true,
                    Name = "SpaceSnoop scan",
                };

                thread.Start();
                threads[index] = thread;
            }
        }
        catch (Exception exception)
        {
            run.Fail(exception);
        }
        finally
        {
            foreach (var thread in threads)
            {
                thread?.Join();
            }
        }

        run.Failure?.Throw();
    }

    private void Work(ScanRun run, ScanTreeStore store, ScanProgress? progress, CancellationToken cancel)
    {
        var writer = new FileSlabWriter(store);
        var nameCache = LocalNameCache.Create();
        var children = new List<ScanChild>();
        var files = new List<FileEntry>();

        try
        {
            foreach (var item in run.Queue.GetConsumingEnumerable(run.Token))
            {
                children.Clear();
                ReadDirectory(item.Path, item.Node, store, writer, nameCache, progress, run.Skips, cancel, children, files);

                run.Expect(children.Count);

                if (item.Branch is null)
                {
                    progress?.SetTopLevelTotal(children.Count);
                }
                else
                {
                    item.Branch.Expect(children.Count);
                }

                foreach (var child in children)
                {
                    run.Queue.Add(
                        child with
                        {
                            Branch = item.Branch ?? new ScanBranch(),
                        },
                        run.Token);
                }

                if (item.Branch is { } branch && branch.Complete())
                {
                    progress?.CompleteTopLevel();
                }

                run.Complete();
            }
        }
        catch (Exception exception)
        {
            run.Fail(exception);
        }
    }

    private void ReadDirectory(
        string path,
        DirectorySpace node,
        ScanTreeStore store,
        FileSlabWriter writer,
        LocalNameCache nameCache,
        ScanProgress? progress,
        SkipJournal skips,
        CancellationToken cancel,
        List<ScanChild> children,
        List<FileEntry> files)
    {
        cancel.ThrowIfCancellationRequested();
        progress?.EnterDirectory(path);

        var first = children.Count;
        files.Clear();
        long ownSize;
        DirectoryScanEnumerator? enumerator = null;

        try
        {
            enumerator = new(path, node, store, nameCache, _log, cancel, children, files);

            while (enumerator.MoveNext())
            {
                _ = enumerator.Current;
            }
        }
        catch (Exception exception) when (IsTraversalError(exception))
        {
            if (skips.TakeFirst())
            {
                _log.ScanDirectorySkipped(exception, FromEnumerationPath(path));
            }
            else
            {
                _log.ScanDirectorySkippedAgain(FromEnumerationPath(path), exception.GetType().Name, exception.Message);
            }

            node.Error();
            progress?.FailDirectory();
        }
        finally
        {
            ownSize = enumerator?.OwnSize ?? 0L;
            enumerator?.Dispose();
        }

        var subDirectories = children.Count == first ? [] : new DirectorySpace[children.Count - first];

        for (var i = 0; i < subDirectories.Length; i++)
        {
            subDirectories[i] = children[first + i].Node;
        }

        writer.WriteFiles(CollectionsMarshal.AsSpan(files), out var fileStart);
        node.SetScanned(store, fileStart, files.Count, ownSize, subDirectories);
        progress?.AddFiles(files.Count, node.Size);
    }

    private sealed class DirectoryScanEnumerator : FileSystemEnumerator<byte>
    {
        private readonly string _path;
        private readonly DirectorySpace _node;
        private readonly ScanTreeStore _store;
        private readonly LocalNameCache _nameCache;
        private readonly ILogger _log;
        private readonly CancellationToken _cancel;
        private readonly List<ScanChild> _children;
        private readonly List<FileEntry> _files;

        public long OwnSize { get; private set; }

        public DirectoryScanEnumerator(
            string path,
            DirectorySpace node,
            ScanTreeStore store,
            LocalNameCache nameCache,
            ILogger log,
            CancellationToken cancel,
            List<ScanChild> children,
            List<FileEntry> files)
            : base(path, Enumeration)
        {
            _path = path;
            _node = node;
            _store = store;
            _nameCache = nameCache;
            _log = log;
            _cancel = cancel;
            _children = children;
            _files = files;
        }

        protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
        {
            _cancel.ThrowIfCancellationRequested();

            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                var linkPath = Path.Join(_path, entry.FileName);

                if (ReparsePoint.IsLink(linkPath, entry.IsDirectory))
                {
                    _log.ScanReparsePointSkipped(FromEnumerationPath(linkPath));
                    return false;
                }
            }

            TimestampEncoding.FromDateTimeOffsets(
                entry.CreationTimeUtc,
                entry.LastAccessTimeUtc,
                out var creationRaw,
                out var lastAccessRaw,
                out var flags);

            var nameId = _nameCache.IsCreated
                ? _nameCache.Intern(_store.Names, entry.FileName)
                : _store.Names.Intern(entry.FileName);

            if (!entry.IsDirectory)
            {
                _files.Add(
                    new()
                    {
                        Size = entry.Length,
                        CreationRaw = creationRaw,
                        LastAccessRaw = lastAccessRaw,
                        NameId = nameId,
                        State = SpaceState.Added,
                        Flags = flags,
                    });
                OwnSize += entry.Length;
                return false;
            }

            var child = new DirectorySpace(_store, nameId, _node, creationRaw, lastAccessRaw, flags);
            _children.Add(new(Path.Join(_path, entry.FileName), child));
            return false;
        }

        protected override byte TransformEntry(ref FileSystemEntry entry)
        {
            return 0;
        }
    }

    private readonly struct LocalNameCache
    {
        private const int SlotCount = 1024;
        private const int SlotMask = SlotCount - 1;
        private const int MaxStackUtf8Chars = 341;

        private readonly CacheSlot[]? _slots;

        public bool IsCreated => _slots is not null;

        private LocalNameCache(CacheSlot[] slots)
        {
            _slots = slots;
        }

        public static LocalNameCache Create()
        {
            var slots = new CacheSlot[SlotCount];

            for (var i = 0; i < slots.Length; i++)
            {
                slots[i].NameId = -1;
            }

            return new(slots);
        }

        public int Intern(NameTable names, ReadOnlySpan<char> chars)
        {
            if (chars.Length > MaxStackUtf8Chars)
            {
                return names.Intern(chars);
            }

            Span<byte> buffer = stackalloc byte[1024];
            var written = Wtf8.FromUtf16(chars, buffer);
            var utf8 = buffer[..written];
            var hash = NameTable.HashUtf8(utf8);
            ref var slot = ref _slots![(int)(hash & SlotMask)];

            if (slot.NameId >= 0 && slot.Hash == hash && names.GetUtf8(slot.NameId).SequenceEqual(utf8))
            {
                return slot.NameId;
            }

            var nameId = names.Intern(utf8, hash);
            slot.Hash = hash;
            slot.NameId = nameId;
            return nameId;
        }

        private struct CacheSlot
        {
            public uint Hash;
            public int NameId;
        }
    }

    private readonly record struct ScanChild(string Path, DirectorySpace Node, ScanBranch? Branch = null);

    private sealed class SkipJournal
    {
        private int _taken;

        public bool TakeFirst()
        {
            return Interlocked.Exchange(ref _taken, 1) == 0;
        }
    }

    private sealed class ScanBranch
    {
        private int _pending = 1;

        public void Expect(int count)
        {
            if (count > 0)
            {
                Interlocked.Add(ref _pending, count);
            }
        }

        public bool Complete()
        {
            return Interlocked.Decrement(ref _pending) == 0;
        }
    }

    private sealed class ScanRun : IDisposable
    {
        private readonly CancellationTokenSource _stop;
        private int _pending = 1;
        private ExceptionDispatchInfo? _failure;

        public BlockingCollection<ScanChild> Queue { get; } = new(new ConcurrentQueue<ScanChild>());

        public SkipJournal Skips { get; } = new();

        public CancellationToken Token => _stop.Token;

        public ExceptionDispatchInfo? Failure => _failure;

        public ScanRun(CancellationToken cancel)
        {
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        }

        public void Expect(int count)
        {
            if (count > 0)
            {
                Interlocked.Add(ref _pending, count);
            }
        }

        public void Complete()
        {
            if (Interlocked.Decrement(ref _pending) == 0)
            {
                Queue.CompleteAdding();
            }
        }

        public void Fail(Exception exception)
        {
            Interlocked.CompareExchange(ref _failure, ExceptionDispatchInfo.Capture(exception), null);
            _stop.Cancel();
        }

        public void Dispose()
        {
            _stop.Dispose();
            Queue.Dispose();
        }
    }
}
