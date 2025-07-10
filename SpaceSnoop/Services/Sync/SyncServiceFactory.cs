using SpaceSnoop.Services.Sync.Executors;
using SpaceSnoop.Services.Sync.Resolvers;

namespace SpaceSnoop.Services.Sync;

/// <summary>
/// Фабрика для создания сервиса синхронизации.
/// </summary>
public class SyncServiceFactory
{
    /// <summary>
    /// Создает экземпляр сервиса синхронизации с настройками по умолчанию.
    /// </summary>
    /// <param name="conflictResolutionStrategy">Стратегия разрешения конфликтов.</param>
    /// <param name="userPrompt">Функция для запроса решения у пользователя (только для стратегии AskUser).</param>
    /// <returns>Настроенный экземпляр SyncService.</returns>
    public static SyncService Create(
        ConflictResolutionStrategy conflictResolutionStrategy = ConflictResolutionStrategy.NewerWins,
        Func<SyncConflictEventArgs, Task<ConflictResolutionStrategy?>>? userPrompt = null)
    {
        var planBuilder = new SyncPlanBuilder();
        var progressReporter = new SyncProgressReporter();
        var executors = CreateExecutors();
        var conflictResolver = ConflictResolverFactory.Create(conflictResolutionStrategy, userPrompt);

        return new(planBuilder, progressReporter, executors, conflictResolver);
    }

    /// <summary>
    /// Создает экземпляр сервиса синхронизации для тестирования.
    /// </summary>
    /// <param name="dryRun">Режим тестирования (без реального выполнения операций).</param>
    /// <param name="conflictResolutionStrategy">Стратегия разрешения конфликтов.</param>
    /// <returns>Настроенный экземпляр SyncService для тестирования.</returns>
    public static SyncService CreateForTesting(
        bool dryRun = true,
        ConflictResolutionStrategy conflictResolutionStrategy = ConflictResolutionStrategy.Skip)
    {
        var service = Create(conflictResolutionStrategy);

        return service;
    }

    /// <summary>
    /// Создает экземпляр сервиса синхронизации с расширенными возможностями логирования.
    /// </summary>
    /// <param name="logFilePath">Путь к файлу логов.</param>
    /// <param name="conflictResolutionStrategy">Стратегия разрешения конфликтов.</param>
    /// <param name="userPrompt">Функция для запроса решения у пользователя.</param>
    /// <returns>Настроенный экземпляр SyncService с логированием.</returns>
    public static SyncService CreateWithLogging(
        string logFilePath,
        ConflictResolutionStrategy conflictResolutionStrategy = ConflictResolutionStrategy.NewerWins,
        Func<SyncConflictEventArgs, Task<ConflictResolutionStrategy?>>? userPrompt = null)
    {
        var service = Create(conflictResolutionStrategy, userPrompt);

        service.ProgressChanged += (sender, args) =>
        {
            LogProgress(logFilePath, args);
        };

        service.SyncCompleted += (sender, result) =>
        {
            LogSyncResult(logFilePath, result);
        };

        return service;
    }

    /// <summary>
    /// Создает список исполнителей операций по умолчанию.
    /// </summary>
    /// <returns>Список исполнителей операций.</returns>
    private static IReadOnlyList<ISyncOperationExecutor> CreateExecutors()
    {
        return new List<ISyncOperationExecutor>
        {
            new FileCopyExecutor(),
            new FileUpdateExecutor(),
            new FileDeleteExecutor(),
            new DirectoryCreateExecutor(),
            new DirectoryDeleteExecutor(),
        }.AsReadOnly();
    }

    /// <summary>
    /// Логирует прогресс синхронизации.
    /// </summary>
    /// <param name="logFilePath">Путь к файлу логов.</param>
    /// <param name="args">Аргументы события прогресса.</param>
    private static void LogProgress(string logFilePath, SyncProgressEventArgs args)
    {
        try
        {
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Прогресс: {args.OperationProgress:F1}% операций, {args.ByteProgress:F1}% байт";

            if (args.CurrentOperation != null)
            {
                logEntry += $" - {args.CurrentOperation}";
            }

            File.AppendAllText(logFilePath, logEntry + Environment.NewLine);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Логирует результат синхронизации.
    /// </summary>
    /// <param name="logFilePath">Путь к файлу логов.</param>
    /// <param name="result">Результат синхронизации.</param>
    private static void LogSyncResult(string logFilePath, SyncResult result)
    {
        try
        {
            var logEntry = $"""
                            [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Синхронизация завершена:
                            - Всего операций: {result.TotalOperations}
                            - Успешных: {result.SuccessfulOperations}
                            - Неудачных: {result.FailedOperations}
                            - Пропущенных: {result.SkippedOperations}
                            - Время выполнения: {result.ElapsedTime}
                            - Синхронизировано байт: {result.TotalSyncedBytes}
                            """;

            if (result.GeneralErrorMessage != null)
            {
                logEntry += $"\n- Ошибка: {result.GeneralErrorMessage}";
            }

            File.AppendAllText(logFilePath, logEntry + Environment.NewLine + Environment.NewLine);
        }
        catch
        {
        }
    }
}
