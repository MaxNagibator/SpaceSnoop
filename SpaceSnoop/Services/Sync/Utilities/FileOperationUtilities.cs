namespace SpaceSnoop.Services.Sync.Utilities;

/// <summary>
/// Утилиты для операций с файлами в процессе синхронизации.
/// </summary>
public static class FileOperationUtilities
{
    /// <summary>
    /// Размер буфера для операций копирования файлов (80KB).
    /// </summary>
    private const int BufferSize = 81920;

    /// <summary>
    /// Асинхронно копирует файл из источника в целевое расположение.
    /// </summary>
    /// <param name="sourcePath">Путь к исходному файлу.</param>
    /// <param name="targetPath">Путь к целевому файлу.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Задача, представляющая асинхронную операцию копирования.</returns>
    /// <exception cref="ArgumentException">Выбрасывается, если пути к файлам пустые или null.</exception>
    /// <exception cref="FileNotFoundException">Выбрасывается, если исходный файл не найден.</exception>
    /// <exception cref="DirectoryNotFoundException">Выбрасывается, если целевая директория не существует.</exception>
    /// <exception cref="UnauthorizedAccessException">Выбрасывается при отсутствии прав доступа.</exception>
    /// <exception cref="IOException">Выбрасывается при ошибках ввода-вывода.</exception>
    public static async Task CopyFileAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Путь к исходному файлу не может быть пустым", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("Путь к целевому файлу не может быть пустым", nameof(targetPath));
        }

        await using var sourceStream = new FileStream(sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.SequentialScan);

        await using var targetStream = new FileStream(targetPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.SequentialScan);

        await sourceStream.CopyToAsync(targetStream, BufferSize, cancellationToken);
    }

    /// <summary>
    /// Копирует временные метки (создание, изменение, доступ) с исходного файла на целевой.
    /// </summary>
    /// <param name="sourceFile">Информация об исходном файле.</param>
    /// <param name="targetFile">Информация о целевом файле.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если один из параметров null.</exception>
    public static void CopyTimestamps(FileInfo sourceFile, FileInfo targetFile)
    {
        ArgumentNullException.ThrowIfNull(sourceFile);
        ArgumentNullException.ThrowIfNull(targetFile);

        targetFile.CreationTime = sourceFile.CreationTime;
        targetFile.LastWriteTime = sourceFile.LastWriteTime;
        targetFile.LastAccessTime = sourceFile.LastAccessTime;
    }

    /// <summary>
    /// Копирует временные метки (создание, изменение, доступ) с исходной директории на целевую.
    /// </summary>
    /// <param name="sourceDirectory">Информация об исходной директории.</param>
    /// <param name="targetDirectory">Информация о целевой директории.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если один из параметров null.</exception>
    public static void CopyTimestamps(DirectoryInfo sourceDirectory, DirectoryInfo targetDirectory)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectory);
        ArgumentNullException.ThrowIfNull(targetDirectory);

        targetDirectory.CreationTime = sourceDirectory.CreationTime;
        targetDirectory.LastWriteTime = sourceDirectory.LastWriteTime;
        targetDirectory.LastAccessTime = sourceDirectory.LastAccessTime;
    }

    /// <summary>
    /// Создает резервную копию файла, если он существует.
    /// </summary>
    /// <param name="filePath">Путь к файлу для резервного копирования.</param>
    /// <param name="backupSuffix">Суффикс для резервной копии.</param>
    /// <returns>True, если резервная копия была создана, иначе false.</returns>
    /// <exception cref="ArgumentException">Выбрасывается, если путь к файлу пустой или null.</exception>
    public static bool CreateBackup(string filePath, string backupSuffix)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Путь к файлу не может быть пустым", nameof(filePath));
        }

        if (File.Exists(filePath) == false)
        {
            return false;
        }

        var backupPath = filePath + backupSuffix;
        File.Copy(filePath, backupPath, true);
        return true;
    }

    /// <summary>
    /// Безопасно создает директорию, если она не существует.
    /// </summary>
    /// <param name="directoryPath">Путь к директории.</param>
    /// <returns>True, если директория была создана или уже существует, иначе false.</returns>
    public static bool EnsureDirectoryExists(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        try
        {
            var directoryInfo = new DirectoryInfo(directoryPath);

            if (directoryInfo.Exists == false)
            {
                directoryInfo.Create();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
