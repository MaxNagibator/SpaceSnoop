using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Runtime.ExceptionServices;
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

        var root = CreateRoot(directory);
        var children = new List<ScanChild>();
        var files = new List<FileSpace>();
        var skips = new SkipJournal();

        ReadDirectory(ToEnumerationPath(directory.FullName), root, progress, skips, cancel, children, files);
        progress?.SetTopLevelTotal(children.Count);

        foreach (var child in children)
        {
            ScanRecursive(child, files, progress, skips, cancel);
            progress?.CompleteTopLevel();
        }

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

        var root = CreateRoot(directory);

        ScanParallel(ToEnumerationPath(directory.FullName), root, Math.Max(1, maxDegreeOfParallelism), progress, cancel);

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
        if (!IsReparsePoint(directory, out var path))
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

    private static DirectorySpace CreateRoot(DirectoryInfo directory)
    {
        return new(directory.Name, null, directory.CreationTime, directory.LastAccessTime);
    }

    private static FileSystemEnumerable<ScanEntry> Enumerate(string path)
    {
        return new(
            path,
            static (ref entry) => new(
                entry.FileName.ToString(),
                entry.Length,
                entry.CreationTimeUtc.LocalDateTime,
                entry.LastAccessTimeUtc.LocalDateTime,
                entry.Attributes,
                entry.IsDirectory),
            Enumeration);
    }

    private static bool IsTraversalError(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;
    }

    private void ScanRecursive(ScanChild item, List<FileSpace> files, ScanProgress? progress, SkipJournal skips, CancellationToken cancel)
    {
        var children = new List<ScanChild>();

        ReadDirectory(item.Path, item.Node, progress, skips, cancel, children, files);

        foreach (var child in children)
        {
            ScanRecursive(child, files, progress, skips, cancel);
        }
    }

    private void ScanParallel(string rootPath, DirectorySpace root, int degree, ScanProgress? progress, CancellationToken cancel)
    {
        using var run = new ScanRun(cancel);
        run.Queue.Add(new(rootPath, root), cancel);

        var threads = new Thread?[degree];

        try
        {
            for (var index = 0; index < degree; index++)
            {
                var thread = new Thread(() => Work(run, progress, cancel))
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

    private void Work(ScanRun run, ScanProgress? progress, CancellationToken cancel)
    {
        var children = new List<ScanChild>();
        var files = new List<FileSpace>();

        try
        {
            foreach (var item in run.Queue.GetConsumingEnumerable(run.Token))
            {
                children.Clear();
                ReadDirectory(item.Path, item.Node, progress, run.Skips, cancel, children, files);

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
        ScanProgress? progress,
        SkipJournal skips,
        CancellationToken cancel,
        List<ScanChild> children,
        List<FileSpace> files)
    {
        cancel.ThrowIfCancellationRequested();
        progress?.EnterDirectory(path);

        var first = children.Count;
        files.Clear();

        try
        {
            foreach (var entry in Enumerate(path))
            {
                cancel.ThrowIfCancellationRequested();

                if (entry.IsReparsePoint)
                {
                    var linkPath = Path.Join(path, entry.Name);

                    if (ReparsePoint.IsLink(linkPath, entry.IsDirectory))
                    {
                        _log.ScanReparsePointSkipped(FromEnumerationPath(linkPath));
                        continue;
                    }
                }

                if (!entry.IsDirectory)
                {
                    files.Add(FileSpace.Create(entry, node));
                    continue;
                }

                var child = new DirectorySpace(entry.Name, node, entry.CreationTime, entry.LastAccessTime);
                children.Add(new(Path.Join(path, entry.Name), child));
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

        var subDirectories = children.Count == first ? [] : new DirectorySpace[children.Count - first];

        for (var i = 0; i < subDirectories.Length; i++)
        {
            subDirectories[i] = children[first + i].Node;
        }

        node.SetScanned(files, subDirectories);
        progress?.AddFiles(files.Count, node.Size);
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
