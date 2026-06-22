using System.Collections.Concurrent;
using System.Security;

namespace SpaceSnoop.Core;

/// <summary>
/// Калькулятор для вычисления занимаемого дискового пространства директории и ее подкаталогов.
/// </summary>
public class DiskSpaceCalculator
{
    /// <summary>
    /// Вычисляет занимаемое дисковое пространство указанной директории и ее подкаталогов.
    /// </summary>
    /// <param name="directory">Директория, для которой нужно вычислить занимаемое дисковое пространство.</param>
    /// <param name="cancel">Токен отмены операции.</param>
    /// <returns>Объект <see cref="DirectorySpace" /> с вычисленной информацией о занимаемом дисковом пространстве.</returns>
    public DirectorySpace Calculate(DirectoryInfo directory, CancellationToken cancel = default)
    {
        return CalculateInner(directory, null, null, cancel);
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
        return CalculateInner(directory, null, progress, cancel);
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
        var counter = new InterlockedInt(Math.Max(1, maxDegreeOfParallelism));

        var directorySpace = CalculateMultithreadedInner(directory, null, counter, progress, cancel);
        directorySpace.FixAbsolutePath(directory);
        return directorySpace;
    }

    private DirectorySpace CalculateInner(DirectoryInfo directory, DirectorySpace? parent, ScanProgress? progress, CancellationToken cancel)
    {
        var directorySpace = new DirectorySpace(directory.Name, parent, directory.CreationTime, directory.LastAccessTime);

        try
        {
            cancel.ThrowIfCancellationRequested();

            progress?.EnterDirectory(directory.FullName);

            var files = directory.GetFiles();
            directorySpace.AddFiles(files);
            progress?.AddFiles(files.Length, directorySpace.Size);

            var isRoot = parent is null;
            var subDirectories = directory.EnumerateDirectories();

            if (isRoot)
            {
                var topLevel = subDirectories as DirectoryInfo[] ?? subDirectories.ToArray();
                progress?.SetTopLevelTotal(topLevel.Length);
                subDirectories = topLevel;
            }

            foreach (var subDirectory in subDirectories)
            {
                cancel.ThrowIfCancellationRequested();
                var subDirectorySpace = CalculateInner(subDirectory, directorySpace, progress, cancel);
                directorySpace.Add(subDirectorySpace);

                if (isRoot)
                {
                    progress?.CompleteTopLevel();
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            directorySpace.Error();
        }

        return directorySpace;
    }

    private DirectorySpace CalculateMultithreadedInner(DirectoryInfo directory, DirectorySpace? parent, InterlockedInt counter, ScanProgress? progress, CancellationToken cancel)
    {
        var directorySpace = DirectorySpace.Create(directory, parent);

        if (cancel.IsCancellationRequested)
        {
            directorySpace.Error();
            cancel.ThrowIfCancellationRequested();
        }

        progress?.EnterDirectory(directory.FullName);

        Span<FileInfo> files;
        DirectoryInfo[] subDirectories;

        try
        {
            files = directory.GetFiles();
            subDirectories = directory.GetDirectories();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            directorySpace.Error();
            return directorySpace;
        }

        directorySpace.AddFiles(files);
        progress?.AddFiles(files.Length, directorySpace.Size);

        AddSubDirectories(directorySpace, subDirectories, counter, progress, cancel);

        return directorySpace;
    }

    private void AddSubDirectories(DirectorySpace directorySpace, DirectoryInfo[] subDirectories, InterlockedInt counter, ScanProgress? progress, CancellationToken cancel)
    {
        counter.Dec();

        ConcurrentBag<DirectorySpace> subDirSpaces = [];
        var availableDegreeOfParallelism = Math.Max(1, counter.Inc());

        // Корень определяется по отсутствию родителя – на нём знаем число «веток» верхнего уровня.
        var isRoot = directorySpace.Parent is null;

        if (isRoot)
        {
            progress?.SetTopLevelTotal(subDirectories.Length);
        }

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = availableDegreeOfParallelism,
            CancellationToken = cancel,
        };

        try
        {
            Parallel.For(0, subDirectories.Length, options, x =>
            {
                var subDir = CalculateMultithreadedInner(subDirectories[x], directorySpace, counter, progress, cancel);
                subDirSpaces.Add(subDir);

                if (isRoot)
                {
                    progress?.CompleteTopLevel();
                }
            });

            if (cancel.IsCancellationRequested)
            {
                directorySpace.Error();
                cancel.ThrowIfCancellationRequested();
            }

            foreach (var subDirSpace in subDirSpaces)
            {
                directorySpace.Add(subDirSpace);
            }
        }
        finally
        {
            counter.Inc();
        }
    }
}
