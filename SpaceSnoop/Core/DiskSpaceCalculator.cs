using System.Collections.Concurrent;
using System.Security;

namespace SpaceSnoop.Core;

/// <summary>
///     Калькулятор для вычисления занимаемого дискового пространства директории и ее подкаталогов.
/// </summary>
public class DiskSpaceCalculator(ILogger<DiskSpaceCalculator> logger) : IDiskSpaceCalculator
{
    /// <inheritdoc />
    public DirectorySpace Calculate(DirectoryInfo directory, CancellationToken cancellationToken = default)
    {
        DirectorySpace directorySpace = new(directory.Name, directory.FullName, directory.CreationTime, directory.LastAccessTime);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<FileInfo> files = directory.EnumerateFiles();
            directorySpace.AddFiles(files);

            IEnumerable<DirectoryInfo> subDirectories = directory.EnumerateDirectories();

            foreach (DirectoryInfo subDirectory in subDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DirectorySpace subDirectorySpace = Calculate(subDirectory, cancellationToken);
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

    /// <inheritdoc />
    public DirectorySpace CalculateMultithreaded(DirectoryInfo directory, CancellationToken cancellationToken = default)
    {
        DirectorySpace directorySpace = new(directory.Name, directory.FullName, directory.CreationTime, directory.LastAccessTime);

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
        IEnumerable<DirectoryInfo> subDirectories = directory.EnumerateDirectories();
        ConcurrentBag<DirectorySpace> subDirSpaces = [];

        Parallel.ForEach(subDirectories, new ParallelOptions { CancellationToken = cancellationToken }, subDirectory =>
        {
            DirectorySpace subDir = CalculateMultithreaded(subDirectory, cancellationToken);
            subDirSpaces.Add(subDir);
        });

        cancellationToken.ThrowIfCancellationRequested();

        foreach (DirectorySpace subDirSpace in subDirSpaces)
        {
            directorySpace.Add(subDirSpace);
        }
    }
}
