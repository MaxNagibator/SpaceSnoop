using System.Collections.Concurrent;
using System.Security;

namespace SpaceSnoop.Core;

/// <summary>
/// Калькулятор для вычисления занимаемого дискового пространства директории и ее подкаталогов.
/// </summary>
public class DiskSpaceCalculator(ILogger<DiskSpaceCalculator> logger)
{
    /// <summary>
    /// Вычисляет занимаемое дисковое пространство указанной директории и ее подкаталогов.
    /// </summary>
    /// <param name="directory">Директория, для которой нужно вычислить занимаемое дисковое пространство.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Объект <see cref="DirectorySpace" /> с вычисленной информацией о занимаемом дисковом пространстве.</returns>
    public DirectorySpace Calculate(DirectoryInfo directory, CancellationToken cancellationToken = default)
    {
        var directorySpace = new DirectorySpace(directory.Name, directory.FullName, directory.CreationTime, directory.LastAccessTime);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var files = directory.EnumerateFiles();
            directorySpace.AddFiles(files);

            var subDirectories = directory.EnumerateDirectories();

            foreach (var subDirectory in subDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var subDirectorySpace = Calculate(subDirectory, cancellationToken);
                directorySpace.Add(subDirectorySpace);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Операция вычисления пространства для каталога {Directory} была отменена.", directory.FullName);
            throw;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            logger.LogError("Отказано в доступе к каталогу: {Directory}", directory.FullName);
        }

        return directorySpace;
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
        var directorySpace = new DirectorySpace(directory.Name, directory.FullName, directory.CreationTime, directory.LastAccessTime);

        if (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Операция вычисления пространства для каталога {Directory} была отменена.", directory.FullName);
            cancellationToken.ThrowIfCancellationRequested();
        }

        IEnumerable<FileInfo> files;

        try
        {
            files = directory.EnumerateFiles();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            logger.LogError("Отказано в доступе к директории: {Directory}", directory.FullName);
            return directorySpace;
        }

        directorySpace.AddFiles(files);

        AddSubDirectories(directorySpace, directory, cancellationToken);

        return directorySpace;
    }

    private void AddSubDirectories(DirectorySpace directorySpace, DirectoryInfo directory, CancellationToken cancellationToken)
    {
        var subDirectories = directory.EnumerateDirectories();
        ConcurrentBag<DirectorySpace> subDirSpaces = [];

        Parallel.ForEach(subDirectories, new()
            { CancellationToken = cancellationToken }, subDirectory =>
        {
            var subDir = CalculateMultithreaded(subDirectory, cancellationToken);
            subDirSpaces.Add(subDir);
        });

        cancellationToken.ThrowIfCancellationRequested();

        foreach (var subDirSpace in subDirSpaces)
        {
            directorySpace.Add(subDirSpace);
        }
    }
}
