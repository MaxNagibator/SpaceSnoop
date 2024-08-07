using System.Collections.Concurrent;
using System.Security;

namespace SpaceSnoop.Core;

/// <summary>
///     Калькулятор для вычисления занимаемого дискового пространства директории и ее подкаталогов.
/// </summary>
public class DiskSpaceCalculator(Action<string>? log = null)
{
    /// <summary>
    ///     Вычисляет занимаемое дисковое пространство указанной директории и ее подкаталогов.
    /// </summary>
    /// <param name="directory">Директория, для которой нужно вычислить занимаемое дисковое пространство.</param>
    /// <returns>Объект <see cref="DirectorySpace" /> с вычисленной информацией о занимаемом дисковом пространстве.</returns>
    public DirectorySpace Calculate(DirectoryInfo directory)
    {
        DirectorySpace directorySpace = new(directory.Name, directory.FullName, directory.CreationTime, directory.LastAccessTime);

        try
        {
            IEnumerable<FileInfo> files = directory.EnumerateFiles();
            directorySpace.AddFiles(files);

            IEnumerable<DirectoryInfo> subDirectories = directory.EnumerateDirectories();

            foreach (DirectoryInfo subDirectory in subDirectories)
            {
                DirectorySpace subDirectorySpace = Calculate(subDirectory);
                directorySpace.Add(subDirectorySpace);
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            log?.Invoke($"Отказано в доступе к каталогу:\n{directory.FullName}\n");
        }

        return directorySpace;
    }

    /// <summary>
    ///     Вычисляет занимаемое дисковое пространство указанной директории и ее подкаталогов в многопоточном режиме.
    /// </summary>
    /// <param name="directory">Директория, для которой нужно вычислить занимаемое дисковое пространство.</param>
    /// <returns>Объект <see cref="DirectorySpace" /> с вычисленной информацией о занимаемом дисковом пространстве.</returns>
    /// <remarks>Повышенное выделение памяти</remarks>
    public DirectorySpace CalculateMultithreaded(DirectoryInfo directory)
    {
        DirectorySpace directorySpace = new(directory.Name, directory.FullName, directory.CreationTime, directory.LastAccessTime);

        try
        {
            IEnumerable<FileInfo> files = directory.EnumerateFiles();
            directorySpace.AddFiles(files);

            IEnumerable<DirectoryInfo> subDirectories = directory.EnumerateDirectories();
            ConcurrentBag<DirectorySpace> subDirSpaces = [];

            Parallel.ForEach(subDirectories, subDirectory =>
            {
                DirectorySpace subDir = CalculateMultithreaded(subDirectory);
                subDirSpaces.Add(subDir);
            });

            foreach (DirectorySpace subDirSpace in subDirSpaces)
            {
                directorySpace.Add(subDirSpace);
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            log?.Invoke($"Отказано в доступе к каталогу:\n{directory.FullName}\n");
        }

        return directorySpace;
    }
}
