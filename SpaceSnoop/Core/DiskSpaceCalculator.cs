using System.Security;

namespace SpaceSnoop.Core;

/// <summary>
///     Калькулятор для вычисления занимаемого дискового пространства директории и ее подкаталогов.
/// </summary>
public class DiskSpaceCalculator
{
    /// <summary>
    ///     Вычисляет занимаемое дисковое пространство указанной директории и ее подкаталогов.
    /// </summary>
    /// <param name="directory">Директория, для которой нужно вычислить занимаемое дисковое пространство.</param>
    /// <returns>Объект <see cref="DirectorySpace" /> с вычисленной информацией о занимаемом дисковом пространстве.</returns>
    public DirectorySpace Calculate(DirectoryInfo directory)
    {
        DirectorySpace directorySpace = new(directory.Name);

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
            // Отказано в доступе
        }

        return directorySpace;
    }
}
