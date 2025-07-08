using System.Text.RegularExpressions;

namespace SpaceSnoop.Core.Domain;

/// <summary>
/// Настройки для операций синхронизации.
/// </summary>
public class SyncSettings
{
    /// <summary>
    /// Создает новые настройки синхронизации со значениями по умолчанию.
    /// </summary>
    public SyncSettings()
    {
        ConflictResolution = ConflictResolutionStrategy.NewerWins;
        IncludeSubdirectories = true;
        MaxFileSizeBytes = null;
        IncludeFileExtensions = [];
        ExcludeFileExtensions = [];
        ExcludeFilePatterns = [];
        ExcludeDirectoryPatterns = [];
        PreserveTimestamps = true;
        CreateBackups = false;
        BackupSuffix = ".backup";
        DryRun = false;
        EnableLogging = true;
        LogFilePath = null;
    }

    /// <summary>
    /// Стратегия разрешения конфликтов.
    /// </summary>
    public ConflictResolutionStrategy ConflictResolution { get; set; }

    /// <summary>
    /// Включать ли подкаталоги в синхронизацию.
    /// </summary>
    public bool IncludeSubdirectories { get; set; }

    /// <summary>
    /// Максимальный размер файла для синхронизации (в байтах). Null означает без ограничений.
    /// </summary>
    public long? MaxFileSizeBytes { get; set; }

    /// <summary>
    /// Фильтры расширений файлов для включения. Пустой список означает все файлы.
    /// </summary>
    public List<string> IncludeFileExtensions { get; set; }

    /// <summary>
    /// Фильтры расширений файлов для исключения.
    /// </summary>
    public List<string> ExcludeFileExtensions { get; set; }

    /// <summary>
    /// Паттерны имен файлов для исключения (поддерживает wildcards).
    /// </summary>
    public List<string> ExcludeFilePatterns { get; set; }

    /// <summary>
    /// Паттерны имен директорий для исключения.
    /// </summary>
    public List<string> ExcludeDirectoryPatterns { get; set; }

    /// <summary>
    /// Сохранять ли временные метки файлов при копировании.
    /// </summary>
    public bool PreserveTimestamps { get; set; }

    /// <summary>
    /// Создавать ли резервные копии перезаписываемых файлов.
    /// </summary>
    public bool CreateBackups { get; set; }

    /// <summary>
    /// Суффикс для резервных копий файлов.
    /// </summary>
    public string BackupSuffix { get; set; }

    /// <summary>
    /// Выполнять ли синхронизацию в режиме "только чтение" (анализ без изменений).
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Логировать ли все операции синхронизации.
    /// </summary>
    public bool EnableLogging { get; set; }

    /// <summary>
    /// Путь к файлу лога. Если null, используется путь по умолчанию.
    /// </summary>
    public string? LogFilePath { get; set; }

    /// <summary>
    /// Создает копию настроек.
    /// </summary>
    /// <returns>Копия текущих настроек.</returns>
    public SyncSettings Clone()
    {
        return new()
        {
            ConflictResolution = ConflictResolution,
            IncludeSubdirectories = IncludeSubdirectories,
            MaxFileSizeBytes = MaxFileSizeBytes,
            IncludeFileExtensions = [..IncludeFileExtensions],
            ExcludeFileExtensions = [..ExcludeFileExtensions],
            ExcludeFilePatterns = [..ExcludeFilePatterns],
            ExcludeDirectoryPatterns = [..ExcludeDirectoryPatterns],
            PreserveTimestamps = PreserveTimestamps,
            CreateBackups = CreateBackups,
            BackupSuffix = BackupSuffix,
            DryRun = DryRun,
            EnableLogging = EnableLogging,
            LogFilePath = LogFilePath,
        };
    }

    /// <summary>
    /// Проверяет, должен ли файл быть включен в синхронизацию на основе настроек фильтрации.
    /// </summary>
    /// <param name="fileName">Имя файла для проверки.</param>
    /// <param name="fileSize">Размер файла в байтах.</param>
    /// <returns>True, если файл должен быть синхронизирован.</returns>
    public bool ShouldIncludeFile(string fileName, long fileSize)
    {
        if (fileSize > MaxFileSizeBytes)
        {
            return false;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (ExcludeFileExtensions.Contains(extension))
        {
            return false;
        }

        if (IncludeFileExtensions.Count > 0 && IncludeFileExtensions.Contains(extension) == false)
        {
            return false;
        }

        return ExcludeFilePatterns.Any(pattern => IsMatchPattern(fileName, pattern));
    }

    /// <summary>
    /// Проверяет, должна ли директория быть включена в синхронизацию.
    /// </summary>
    /// <param name="directoryName">Имя директории для проверки.</param>
    /// <returns>True, если директория должна быть синхронизирована.</returns>
    public bool ShouldIncludeDirectory(string directoryName)
    {
        return ExcludeDirectoryPatterns.Any(pattern => IsMatchPattern(directoryName, pattern));
    }

    /// <summary>
    /// Простая проверка соответствия паттерну с поддержкой wildcards (* и ?).
    /// </summary>
    /// <param name="input">Строка для проверки.</param>
    /// <param name="pattern">Паттерн с wildcards.</param>
    /// <returns>True, если строка соответствует паттерну.</returns>
    private static bool IsMatchPattern(string input, string pattern)
    {
        var regexPattern = pattern
            .Replace("*", ".*")
            .Replace("?", ".");

        return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
    }
}
