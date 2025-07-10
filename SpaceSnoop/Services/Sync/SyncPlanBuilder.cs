using System.Text.RegularExpressions;

namespace SpaceSnoop.Services.Sync;

/// <summary>
/// Строитель планов синхронизации.
/// </summary>
public class SyncPlanBuilder
{
    private readonly Dictionary<string, Regex> _compiledFilePatterns = new();
    private readonly Dictionary<string, Regex> _compiledDirectoryPatterns = new();

    /// <summary>
    /// Создает план синхронизации между исходной и целевой директориями.
    /// </summary>
    /// <param name="sourceDirectory">Исходная директория.</param>
    /// <param name="targetDirectory">Целевая директория.</param>
    /// <param name="settings">Настройки синхронизации.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Список операций синхронизации.</returns>
    public async Task<IReadOnlyList<SyncOperation>> BuildSyncPlanAsync(
        DirectorySpace sourceDirectory,
        DirectorySpace targetDirectory,
        SyncSettings settings,
        CancellationToken cancellationToken = default)
    {
        CompileRegexPatterns(settings);

        var operations = new List<SyncOperation>();

        await BuildSyncPlanRecursiveAsync(sourceDirectory,
            targetDirectory,
            operations,
            settings,
            cancellationToken);

        return operations.OrderBy(x => x.Priority).ToList().AsReadOnly();
    }

    /// <summary>
    /// Очищает кэш скомпилированных регулярных выражений.
    /// </summary>
    public void ClearCompiledPatterns()
    {
        _compiledFilePatterns.Clear();
        _compiledDirectoryPatterns.Clear();
    }

    /// <summary>
    /// Проверяет, отличаются ли файлы.
    /// </summary>
    private static async Task<bool> FilesAreDifferentAsync(FileSpace sourceFile, string targetFilePath)
    {
        if (await FileExistsAsync(targetFilePath) == false)
        {
            return true;
        }

        var targetFileInfo = await GetFileInfoAsync(targetFilePath);
        return sourceFile.Size != targetFileInfo.Length || sourceFile.LastAccessTime != targetFileInfo.LastWriteTime;
    }

    /// <summary>
    /// Определяет операцию для файла.
    /// </summary>
    private static async Task<SyncOperation?> DetermineFileOperationAsync(
        FileSpace sourceFile,
        string sourceFilePath,
        string targetFilePath)
    {
        if (await FileExistsAsync(targetFilePath) == false)
        {
            return new(SyncOperationType.Copy,
                sourceFilePath,
                targetFilePath,
                false,
                sourceFile.Size,
                $"Копирование файла {sourceFile.Name}");
        }

        if (await FilesAreDifferentAsync(sourceFile, targetFilePath))
        {
            return new(SyncOperationType.Update,
                sourceFilePath,
                targetFilePath,
                false,
                sourceFile.Size,
                $"Обновление файла {sourceFile.Name}");
        }

        return new(SyncOperationType.Skip,
            sourceFilePath,
            targetFilePath,
            false,
            sourceFile.Size,
            $"Пропуск файла {sourceFile.Name} (файлы идентичны)");
    }

    /// <summary>
    /// Проверяет, существует ли директория по указанному пути.
    /// </summary>
    /// <param name="directoryPath">Путь к директории.</param>
    /// <returns>True, если директория существует, иначе false.</returns>
    private static Task<bool> DirectoryExistsAsync(string directoryPath)
    {
        return Task.FromResult(Directory.Exists(directoryPath));
    }

    /// <summary>
    /// Проверяет, существует ли файл по указанному пути.
    /// </summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <returns>True, если файл существует, иначе false.</returns>
    private static Task<bool> FileExistsAsync(string filePath)
    {
        return Task.FromResult(File.Exists(filePath));
    }

    /// <summary>
    /// Получает информацию о файле по указанному пути.
    /// </summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <returns>Информация о файле.</returns>
    private static Task<FileInfo> GetFileInfoAsync(string filePath)
    {
        return Task.FromResult(new FileInfo(filePath));
    }

    /// <summary>
    /// Рекурсивно строит план синхронизации.
    /// </summary>
    private async Task BuildSyncPlanRecursiveAsync(
        DirectorySpace sourceDirectory,
        DirectorySpace targetDirectory,
        List<SyncOperation> operations,
        SyncSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sourceDirectoryPath = sourceDirectory.AbsolutePath;
        var targetDirectoryPath = targetDirectory.AbsolutePath;

        if (await DirectoryExistsAsync(targetDirectoryPath) == false)
        {
            operations.Add(new(SyncOperationType.CreateDirectory,
                sourceDirectoryPath,
                targetDirectoryPath,
                true,
                description: $"Создание директории {targetDirectory.Name}"));
        }

        await ProcessFilesAsync(sourceDirectory, sourceDirectoryPath, targetDirectoryPath, operations, settings, cancellationToken);

        if (settings.IncludeSubdirectories)
        {
            await ProcessSubdirectoriesAsync(sourceDirectory, sourceDirectoryPath, targetDirectoryPath, operations, settings, cancellationToken);
        }
    }

    /// <summary>
    /// Рекурсивно строит план синхронизации.
    /// </summary>
    private async Task BuildSyncPlanRecursiveAsync(
        DirectorySpace sourceDirectory,
        string sourceDirectoryPath,
        string targetDirectoryPath,
        List<SyncOperation> operations,
        SyncSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (await DirectoryExistsAsync(targetDirectoryPath) == false)
        {
            operations.Add(new(SyncOperationType.CreateDirectory,
                sourceDirectoryPath,
                targetDirectoryPath,
                true,
                description: $"Создание директории {sourceDirectory.Name}"));
        }

        await ProcessFilesAsync(sourceDirectory, sourceDirectoryPath, targetDirectoryPath, operations, settings, cancellationToken);

        if (settings.IncludeSubdirectories)
        {
            await ProcessSubdirectoriesAsync(sourceDirectory, sourceDirectoryPath, targetDirectoryPath, operations, settings, cancellationToken);
        }
    }

    /// <summary>
    /// Обрабатывает файлы в директории.
    /// </summary>
    private async Task ProcessFilesAsync(
        DirectorySpace sourceDirectory,
        string sourceDirectoryPath,
        string targetDirectoryPath,
        List<SyncOperation> operations,
        SyncSettings settings,
        CancellationToken cancellationToken)
    {
        foreach (var sourceFile in sourceDirectory.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ShouldProcessFile(sourceFile, settings) == false)
            {
                continue;
            }

            var sourceFilePath = Path.Combine(sourceDirectoryPath, sourceFile.Name);
            var targetFilePath = Path.Combine(targetDirectoryPath, sourceFile.Name);

            var operation = await DetermineFileOperationAsync(sourceFile, sourceFilePath, targetFilePath);

            if (operation != null)
            {
                operations.Add(operation);
            }
        }
    }

    /// <summary>
    /// Обрабатывает поддиректории.
    /// </summary>
    private async Task ProcessSubdirectoriesAsync(
        DirectorySpace sourceDirectory,
        string sourceDirectoryPath,
        string targetDirectoryPath,
        List<SyncOperation> operations,
        SyncSettings settings,
        CancellationToken cancellationToken)
    {
        foreach (var sourceSubdirectory in sourceDirectory.SubDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ShouldProcessDirectory(sourceSubdirectory, settings) == false)
            {
                continue;
            }

            var sourceSubdirectoryPath = Path.Combine(sourceDirectoryPath, sourceSubdirectory.Name);
            var targetSubdirectoryPath = Path.Combine(targetDirectoryPath, sourceSubdirectory.Name);

            await BuildSyncPlanRecursiveAsync(sourceSubdirectory, sourceSubdirectoryPath, targetSubdirectoryPath, operations, settings, cancellationToken);
        }
    }

    /// <summary>
    /// Проверяет, нужно ли обрабатывать файл.
    /// </summary>
    private bool ShouldProcessFile(FileSpace file, SyncSettings settings)
    {
        if (settings.MaxFileSizeBytes.HasValue && file.Size > settings.MaxFileSizeBytes.Value)
        {
            return false;
        }

        var fileName = file.Name;
        var fileExtension = Path.GetExtension(fileName).ToLowerInvariant();

        if (settings.IncludeFileExtensions.Count > 0 && settings.IncludeFileExtensions.Contains(fileExtension) == false)
        {
            return false;
        }

        if (settings.ExcludeFileExtensions.Contains(fileExtension))
        {
            return false;
        }

        foreach (var pattern in settings.ExcludeFilePatterns)
        {
            if (_compiledFilePatterns.TryGetValue(pattern, out var compiledRegex))
            {
                if (compiledRegex.IsMatch(fileName))
                {
                    return false;
                }
            }
            else
            {
                throw new InvalidOperationException($"Паттерн '{pattern}' не найден в кэше скомпилированных регулярных выражений. Убедитесь, что CompileRegexPatterns() был вызван перед фильтрацией.");
            }
        }

        return true;
    }

    /// <summary>
    /// Проверяет, нужно ли обрабатывать директорию.
    /// </summary>
    private bool ShouldProcessDirectory(DirectorySpace directory, SyncSettings settings)
    {
        var directoryName = directory.Name;

        foreach (var pattern in settings.ExcludeDirectoryPatterns)
        {
            if (_compiledDirectoryPatterns.TryGetValue(pattern, out var compiledRegex))
            {
                if (compiledRegex.IsMatch(directoryName))
                {
                    return false;
                }
            }
            else
            {
                throw new InvalidOperationException($"Паттерн '{pattern}' не найден в кэше скомпилированных регулярных выражений. Убедитесь, что CompileRegexPatterns() был вызван перед фильтрацией.");
            }
        }

        return true;
    }

    /// <summary>
    /// Компилирует паттерны регулярных выражений для оптимизации производительности.
    /// </summary>
    /// <param name="settings">Настройки синхронизации с паттернами.</param>
    /// <exception cref="ArgumentException">
    /// Выбрасывается при обнаружении одного или нескольких некорректных паттернов
    /// регулярных выражений.
    /// </exception>
    private void CompileRegexPatterns(SyncSettings settings)
    {
        var invalidFilePatterns = new List<string>();
        var invalidDirectoryPatterns = new List<string>();

        foreach (var pattern in settings.ExcludeFilePatterns.Where(pattern => _compiledFilePatterns.ContainsKey(pattern) == false))
        {
            try
            {
                _compiledFilePatterns[pattern] = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
            catch (ArgumentException exception)
            {
                invalidFilePatterns.Add($"'{pattern}': {exception.Message}");
            }
        }

        foreach (var pattern in settings.ExcludeDirectoryPatterns.Where(pattern => _compiledDirectoryPatterns.ContainsKey(pattern) == false))
        {
            try
            {
                _compiledDirectoryPatterns[pattern] = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
            catch (ArgumentException exception)
            {
                invalidDirectoryPatterns.Add($"'{pattern}': {exception.Message}");
            }
        }

        if (invalidFilePatterns.Count <= 0 && invalidDirectoryPatterns.Count <= 0)
        {
            return;
        }

        var errorMessage = "Обнаружены некорректные паттерны регулярных выражений:";

        if (invalidFilePatterns.Count > 0)
        {
            errorMessage += $"\nПаттерны исключения файлов:\n  - {string.Join("\n  - ", invalidFilePatterns)}";
        }

        if (invalidDirectoryPatterns.Count > 0)
        {
            errorMessage += $"\nПаттерны исключения директорий:\n  - {string.Join("\n  - ", invalidDirectoryPatterns)}";
        }

        throw new ArgumentException(errorMessage);
    }
}
