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
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Объект <see cref="DirectorySpace" /> с вычисленной информацией о занимаемом дисковом пространстве.</returns>
    public DirectorySpace Calculate(DirectoryInfo directory, CancellationToken cancellationToken = default)
    {
        return CalculateInner(directory, null, cancellationToken);
    }

    /// <summary>
    /// Вычисляет занимаемое дисковое пространство указанной директории и ее подкаталогов в многопоточном режиме.
    /// </summary>
    /// <param name="directory">Директория, для которой нужно вычислить занимаемое дисковое пространство.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Объект <see cref="DirectorySpace" /> с вычисленной информацией о занимаемом дисковом пространстве.</returns>
    /// <remarks>Повышенное выделение памяти</remarks>
    public DirectorySpace CalculateMultithreaded(DirectoryInfo directory, CancellationToken cancellationToken = default)
    {
        var maxDegreeOfParallelism = Environment.ProcessorCount;
        var counter = new InterlockedInt(maxDegreeOfParallelism);

        var directorySpace = CalculateMultithreadedInner(directory, null, counter, cancellationToken);
        directorySpace.FixAbsolutePath(directory);
        return directorySpace;
    }

    private DirectorySpace CalculateInner(DirectoryInfo directory, DirectorySpace? parent, CancellationToken cancellationToken)
    {
        var directorySpace = new DirectorySpace(directory.Name, parent, directory.CreationTime, directory.LastAccessTime);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var files = directory.GetFiles();
            directorySpace.AddFiles(files);

            var subDirectories = directory.EnumerateDirectories();

            foreach (var subDirectory in subDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var subDirectorySpace = CalculateInner(subDirectory, directorySpace, cancellationToken);
                directorySpace.Add(subDirectorySpace);
            }
        }
        catch (OperationCanceledException)
        {
            //logger.LogInformation("Операция вычисления пространства для каталога {Directory} была отменена.", directory.FullName);
            throw;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            //logger.LogError("Отказано в доступе к каталогу: {Directory}", directory.FullName);
            directorySpace.Error();
        }

        return directorySpace;
    }

    private DirectorySpace CalculateMultithreadedInner(DirectoryInfo directory, DirectorySpace? parent, InterlockedInt counter, CancellationToken cancellationToken)
    {
        var directorySpace = DirectorySpace.Create(directory, parent);

        if (cancellationToken.IsCancellationRequested)
        {
            //logger.LogInformation("Операция вычисления пространства для каталога {Directory} была отменена.", directory.FullName);
            directorySpace.Error();
            cancellationToken.ThrowIfCancellationRequested();
        }

        Span<FileInfo> files;

        try
        {
            files = directory.GetFiles();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            //logger.LogError("Отказано в доступе к директории: {Directory}", directory.FullName);
            directorySpace.Error();
            return directorySpace;
        }

        directorySpace.AddFiles(files);

        AddSubDirectories(directorySpace, directory.GetDirectories(), counter, cancellationToken);

        return directorySpace;
    }

    private void AddSubDirectories(DirectorySpace directorySpace, DirectoryInfo[] subDirectories, InterlockedInt counter, CancellationToken cancellationToken)
    {
        counter.Dec();

        ConcurrentBag<DirectorySpace> subDirSpaces = [];
        var availableDegreeOfParallelism = Math.Max(1, counter.Inc());

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = availableDegreeOfParallelism,
            CancellationToken = cancellationToken,
        };

        try
        {
            Parallel.For(0, subDirectories.Length, options, i =>
            {
                var subDir = CalculateMultithreadedInner(subDirectories[i], directorySpace, counter, cancellationToken);
                subDirSpaces.Add(subDir);
            });

            if (cancellationToken.IsCancellationRequested)
            {
                directorySpace.Error();
                cancellationToken.ThrowIfCancellationRequested();
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
