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
    public DirectorySpace Calculate(DirectoryInfo directory, ScanProgress? progress, CancellationToken cancel = default)
    {
        var root = CreateRoot(directory);
        var children = new List<ScanChild>();

        ReadDirectory(directory.FullName, root, progress, cancel, children);
        progress?.SetTopLevelTotal(children.Count);

        foreach (var child in children)
        {
            ScanRecursive(child, progress, cancel);
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
    public DirectorySpace CalculateMultithreaded(DirectoryInfo directory, int maxDegreeOfParallelism, ScanProgress? progress, CancellationToken cancel = default)
    {
        var root = CreateRoot(directory);

        ScanParallel(directory.FullName, root, Math.Max(1, maxDegreeOfParallelism), progress, cancel);

        root.AggregateTotals();
        root.FixAbsolutePath(directory);
        return root;
    }

    private static DirectorySpace CreateRoot(DirectoryInfo directory)
    {
        return new(directory.Name, null, directory.CreationTime, directory.LastAccessTime);
    }

    private static FileSystemEnumerable<ScanEntry> Enumerate(string path)
    {
        return new(path,
            static (ref FileSystemEntry entry) => new ScanEntry(entry.FileName.ToString(),
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

    private void ScanRecursive(ScanChild item, ScanProgress? progress, CancellationToken cancel)
    {
        var children = new List<ScanChild>();

        ReadDirectory(item.Path, item.Node, progress, cancel, children);

        foreach (var child in children)
        {
            ScanRecursive(child, progress, cancel);
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

        try
        {
            foreach (var item in run.Queue.GetConsumingEnumerable(run.Token))
            {
                children.Clear();
                ReadDirectory(item.Path, item.Node, progress, cancel, children);

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
                    run.Queue.Add(child with { Branch = item.Branch ?? new ScanBranch() }, run.Token);
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

    private void ReadDirectory(string path, DirectorySpace node, ScanProgress? progress, CancellationToken cancel, List<ScanChild> children)
    {
        cancel.ThrowIfCancellationRequested();
        progress?.EnterDirectory(path);

        var files = 0;

        try
        {
            foreach (var entry in Enumerate(path))
            {
                cancel.ThrowIfCancellationRequested();

                if (!entry.IsDirectory)
                {
                    node.AddScannedFile(entry);
                    files++;
                    continue;
                }

                if (entry.IsReparsePoint)
                {
                    _log.ScanReparsePointSkipped(Path.Join(path, entry.Name));
                    continue;
                }

                var child = new DirectorySpace(entry.Name, node, entry.CreationTime, entry.LastAccessTime);
                node.AddScannedDirectory(child);
                children.Add(new(Path.Join(path, entry.Name), child));
            }
        }
        catch (Exception exception) when (IsTraversalError(exception))
        {
            _log.ScanDirectorySkipped(exception, path);
            node.Error();
            progress?.FailDirectory();
        }

        node.SealFiles();
        progress?.AddFiles(files, node.Size);
    }

    private readonly record struct ScanChild(string Path, DirectorySpace Node, ScanBranch? Branch = null);

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

        public ScanRun(CancellationToken cancel)
        {
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        }

        public BlockingCollection<ScanChild> Queue { get; } = new(new ConcurrentQueue<ScanChild>());

        public CancellationToken Token => _stop.Token;

        public ExceptionDispatchInfo? Failure => _failure;

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
