using SpaceSnoop.Services.Sync.Executors;
using SpaceSnoop.Services.Sync.Resolvers;
using System.Diagnostics;

namespace SpaceSnoop.Services.Sync;

/// <summary>
/// Сервис синхронизации.
/// </summary>
public class SyncService
{
    private readonly SyncPlanBuilder _planBuilder;
    private readonly SyncProgressReporter _progressReporter;
    private readonly IReadOnlyList<ISyncOperationExecutor> _executors;
    private readonly ISyncConflictResolver _conflictResolver;

    /// <summary>
    /// Создает новый экземпляр сервиса синхронизации.
    /// </summary>
    /// <param name="planBuilder">Строитель планов синхронизации.</param>
    /// <param name="progressReporter">Репортер прогресса.</param>
    /// <param name="executors">Список исполнителей операций.</param>
    /// <param name="conflictResolver">Разрешитель конфликтов.</param>
    public SyncService(
        SyncPlanBuilder planBuilder,
        SyncProgressReporter progressReporter,
        IReadOnlyList<ISyncOperationExecutor> executors,
        ISyncConflictResolver conflictResolver)
    {
        _planBuilder = planBuilder;
        _progressReporter = progressReporter;
        _executors = executors;
        _conflictResolver = conflictResolver;

        _progressReporter.ProgressChanged += (_, args) => ProgressChanged?.Invoke(this, args);
    }

    /// <summary>
    /// Событие для запроса разрешения конфликта у пользователя.
    /// </summary>
    public event EventHandler<SyncConflictEventArgs>? ConflictResolutionRequested;

    /// <summary>
    /// Событие для отслеживания прогресса синхронизации.
    /// </summary>
    public event EventHandler<SyncProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Событие для уведомления о завершении синхронизации.
    /// </summary>
    public event EventHandler<SyncResult>? SyncCompleted;

    /// <summary>
    /// Проверяет, можно ли выполнить синхронизацию с указанными параметрами.
    /// </summary>
    /// <param name="sourceDirectory">Исходная директория.</param>
    /// <param name="targetDirectory">Целевая директория.</param>
    /// <param name="settings">Настройки синхронизации.</param>
    /// <returns>True, если синхронизация возможна, иначе false.</returns>
    public static bool CanSync(DirectorySpace sourceDirectory, DirectorySpace targetDirectory, SyncSettings settings)
    {
        var sourceDirectoryPath = sourceDirectory.AbsolutePath;
        var targetDirectoryPath = targetDirectory.AbsolutePath;

        var sourceDirectoryInfo = new DirectoryInfo(sourceDirectoryPath);

        if (sourceDirectoryInfo.Exists == false)
        {
            return false;
        }

        try
        {
            var targetDirectoryInfo = new DirectoryInfo(targetDirectoryPath);

            if (targetDirectoryInfo.Exists == false)
            {
                targetDirectoryInfo.Create();
                targetDirectoryInfo.Delete();
            }
            else
            {
                var testFile = Path.Combine(targetDirectoryPath, $"test_{Guid.NewGuid()}.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Планирует операции синхронизации между исходной и целевой директориями.
    /// </summary>
    /// <param name="sourceDirectory">Исходная директория.</param>
    /// <param name="targetDirectory">Целевая директория.</param>
    /// <param name="settings">Настройки синхронизации.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Список запланированных операций синхронизации.</returns>
    public Task<IReadOnlyList<SyncOperation>> PlanSyncAsync(
        DirectorySpace sourceDirectory,
        DirectorySpace targetDirectory,
        SyncSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (CanSync(sourceDirectory, targetDirectory, settings) == false)
        {
            throw new InvalidOperationException("Невозможно выполнить синхронизацию с указанными параметрами");
        }

        MarkDirectoryTreeAsPending(sourceDirectory);

        return _planBuilder.BuildSyncPlanAsync(sourceDirectory, targetDirectory, settings, cancellationToken);
    }

    /// <summary>
    /// Выполняет синхронизацию между исходной и целевой директориями.
    /// </summary>
    /// <param name="sourceDirectory">Исходная директория.</param>
    /// <param name="targetDirectory">Целевая директория.</param>
    /// <param name="settings">Настройки синхронизации.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Результат синхронизации.</returns>
    public async Task<SyncResult> SyncAsync(
        DirectorySpace sourceDirectory,
        DirectorySpace targetDirectory,
        SyncSettings settings,
        CancellationToken cancellationToken = default)
    {
        var operations = await PlanSyncAsync(sourceDirectory, targetDirectory, settings, cancellationToken);
        return await ExecuteSyncPlanAsync(operations, sourceDirectory, settings, cancellationToken);
    }

    /// <summary>
    /// Выполняет заранее запланированные операции синхронизации.
    /// </summary>
    /// <param name="operations">Список операций для выполнения.</param>
    /// <param name="sourceDirectory">Исходная директория для поиска элементов.</param>
    /// <param name="settings">Настройки синхронизации.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Результат синхронизации.</returns>
    public async Task<SyncResult> ExecuteSyncPlanAsync(
        IReadOnlyList<SyncOperation> operations,
        DirectorySpace sourceDirectory,
        SyncSettings settings,
        CancellationToken cancellationToken = default)
    {
        var context = InitializeSyncContext(operations);

        try
        {
            await ExecuteOperationsAsync(operations, sourceDirectory, settings, context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            context.GeneralErrorMessage = "Синхронизация была отменена пользователем";
        }
        catch (Exception exception)
        {
            context.GeneralErrorMessage = $"Произошла критическая ошибка: {exception.Message}";
        }
        finally
        {
            FinalizeSyncContext(context);
        }

        var result = CreateSyncResult(operations, context);
        SyncCompleted?.Invoke(this, result);
        return result;
    }

    /// <summary>
    /// Отмечает все элементы в дереве директорий как ожидающие синхронизации.
    /// </summary>
    /// <param name="directory">Корневая директория.</param>
    private static void MarkDirectoryTreeAsPending(DirectorySpace directory)
    {
        directory.MarkSyncPending();

        foreach (var file in directory.Files)
        {
            file.MarkSyncPending();
        }

        foreach (var subDirectory in directory.SubDirectories)
        {
            MarkDirectoryTreeAsPending(subDirectory);
        }
    }

    /// <summary>
    /// Рекурсивно ищет элемент SpaceBase по пути.
    /// </summary>
    /// <param name="directory">Директория для поиска.</param>
    /// <param name="targetPath">Целевой путь.</param>
    /// <returns>Найденный элемент или null.</returns>
    private static SpaceBase? FindSpaceElementRecursive(DirectorySpace directory, string targetPath)
    {
        var directoryPath = directory.AbsolutePath;
        return FindSpaceElementRecursive(directory, directoryPath, targetPath);
    }

    /// <summary>
    /// Рекурсивно ищет элемент SpaceBase по пути с оптимизацией производительности через кэшированные пути.
    /// </summary>
    /// <param name="directory">Директория для поиска.</param>
    /// <param name="directoryPath">Кэшированный путь текущей директории.</param>
    /// <param name="targetPath">Целевой путь.</param>
    /// <returns>Найденный элемент или null.</returns>
    private static SpaceBase? FindSpaceElementRecursive(DirectorySpace directory, string directoryPath, string targetPath)
    {
        if (string.Equals(directoryPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return directory;
        }

        foreach (var file in directory.Files)
        {
            var filePath = Path.Combine(directoryPath, file.Name);

            if (string.Equals(filePath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        foreach (var subDirectory in directory.SubDirectories)
        {
            var subDirectoryPath = Path.Combine(directoryPath, subDirectory.Name);
            var found = FindSpaceElementRecursive(subDirectory, subDirectoryPath, targetPath);

            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Обрабатывает операцию пропуска.
    /// </summary>
    private static bool HandleSkipOperation(SyncOperation operation, SpaceBase? spaceElement)
    {
        spaceElement?.MarkSyncCompleted(operation.OperationType, operation.TargetPath);
        return true;
    }

    /// <summary>
    /// Обрабатывает конфликт с выбором целевого файла.
    /// </summary>
    private static bool HandleUseTargetConflict(SyncOperation operation, SpaceBase? spaceElement)
    {
        spaceElement?.MarkSyncCompleted(operation.OperationType, operation.TargetPath);
        return true;
    }

    /// <summary>
    /// Обрабатывает конфликт с пропуском операции.
    /// </summary>
    private static bool HandleSkipConflict(SyncOperation operation, SpaceBase? spaceElement)
    {
        spaceElement?.MarkSyncCompleted(SyncOperationType.Skip, operation.TargetPath);
        return true;
    }

    /// <summary>
    /// Находит элемент SpaceBase по пути в дереве директорий.
    /// </summary>
    /// <param name="sourceDirectory">Корневая директория для поиска.</param>
    /// <param name="path">Путь к элементу.</param>
    /// <returns>Найденный элемент или null.</returns>
    private static SpaceBase? FindSpaceElement(DirectorySpace sourceDirectory, string path)
    {
        return FindSpaceElementRecursive(sourceDirectory, path);
    }

    /// <summary>
    /// Обрабатывает результат выполнения операции.
    /// </summary>
    private static void ProcessOperationResult(SyncOperation operation, bool success, SyncContext context)
    {
        if (success)
        {
            if (operation.OperationType == SyncOperationType.Skip)
            {
                context.SkippedOperations++;
            }
            else
            {
                context.SuccessfulOperations++;

                if (operation.FileSize.HasValue)
                {
                    context.TotalSyncedBytes += operation.FileSize.Value;
                }
            }

            context.CompletedOperations.Add(operation);
        }
        else
        {
            context.FailedOperations++;
            context.FailedOperationDetails.Add((operation, "Операция завершилась неудачно"));
        }
    }

    /// <summary>
    /// Создает результат синхронизации на основе контекста.
    /// </summary>
    private static SyncResult CreateSyncResult(IReadOnlyList<SyncOperation> operations, SyncContext context)
    {
        return new(operations.Count,
            context.SuccessfulOperations,
            context.FailedOperations,
            context.SkippedOperations,
            context.CompletedOperations.AsReadOnly(),
            context.FailedOperationDetails.AsReadOnly(),
            context.Stopwatch.Elapsed,
            context.TotalSyncedBytes,
            context.StartTime,
            context.EndTime,
            context.GeneralErrorMessage);
    }

    /// <summary>
    /// Выполняет операцию синхронизации с интеграцией в domain model.
    /// </summary>
    private async Task<bool> ExecuteOperationAsync(
        SyncOperation operation,
        DirectorySpace sourceDirectory,
        SyncSettings settings,
        CancellationToken cancellationToken)
    {
        var spaceElement = FindSpaceElement(sourceDirectory, operation.SourcePath);

        try
        {
            spaceElement?.MarkSyncStarted();

            if (operation.OperationType == SyncOperationType.Skip)
            {
                return HandleSkipOperation(operation, spaceElement);
            }

            if (operation.OperationType == SyncOperationType.Copy)
            {
                var conflictHandled = await HandleCopyConflictAsync(operation, spaceElement, settings, cancellationToken);

                if (conflictHandled.HasValue)
                {
                    return conflictHandled.Value;
                }
            }

            return await ExecuteOperationAsync(operation, spaceElement, settings, cancellationToken);
        }
        catch (Exception exception)
        {
            spaceElement?.MarkSyncFailed(exception.Message);
            throw;
        }
    }

    /// <summary>
    /// Обрабатывает конфликты при копировании файлов.
    /// </summary>
    /// <returns>Результат обработки конфликта или null, если конфликта нет.</returns>
    private async Task<bool?> HandleCopyConflictAsync(
        SyncOperation operation,
        SpaceBase? spaceElement,
        SyncSettings settings,
        CancellationToken cancellationToken)
    {
        var targetFile = new FileInfo(operation.TargetPath);

        if (targetFile.Exists == false)
        {
            return null;
        }

        var sourceFile = new FileInfo(operation.SourcePath);
        var conflictResult = await _conflictResolver.ResolveConflictAsync(operation, sourceFile, targetFile, settings, cancellationToken);

        return conflictResult.Action switch
        {
            ConflictAction.UseTarget => HandleUseTargetConflict(operation, spaceElement),
            ConflictAction.Skip => HandleSkipConflict(operation, spaceElement),
            ConflictAction.AskUser => HandleUserConflict(operation, spaceElement, conflictResult, sourceFile, targetFile),
            _ => null,
        };
    }

    /// <summary>
    /// Обрабатывает конфликт с запросом решения у пользователя.
    /// </summary>
    private bool HandleUserConflict(
        SyncOperation operation,
        SpaceBase? spaceElement,
        ConflictResolutionResult conflictResult,
        FileInfo sourceFile,
        FileInfo targetFile)
    {
        var conflictArgs = new SyncConflictEventArgs(operation, conflictResult.Reason ?? "Конфликт файлов", sourceFile, targetFile);
        ConflictResolutionRequested?.Invoke(this, conflictArgs);

        if (conflictArgs.Resolution == ConflictResolutionStrategy.Skip)
        {
            spaceElement?.MarkSyncConflict(conflictResult.Reason ?? "Пользователь выбрал пропуск");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Выполняет операцию синхронизации.
    /// </summary>
    private async Task<bool> ExecuteOperationAsync(
        SyncOperation operation,
        SpaceBase? spaceElement,
        SyncSettings settings,
        CancellationToken cancellationToken)
    {
        var executor = _executors.FirstOrDefault(e => e.CanExecute(operation));

        if (executor == null)
        {
            var errorMessage = $"Не найден исполнитель для операции {operation.OperationType}";
            spaceElement?.MarkSyncFailed(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        var success = await executor.ExecuteAsync(operation, settings, cancellationToken);

        if (success)
        {
            spaceElement?.MarkSyncCompleted(operation.OperationType, operation.TargetPath);
        }
        else
        {
            spaceElement?.MarkSyncFailed("Операция завершилась неудачно");
        }

        return success;
    }

    /// <summary>
    /// Инициализирует контекст синхронизации.
    /// </summary>
    /// <param name="operations">Список операций для выполнения.</param>
    /// <returns>Инициализированный контекст синхронизации.</returns>
    private SyncContext InitializeSyncContext(IReadOnlyList<SyncOperation> operations)
    {
        var context = new SyncContext
        {
            StartTime = DateTime.Now,
            Stopwatch = Stopwatch.StartNew(),
        };

        _progressReporter.Initialize(operations);
        return context;
    }

    /// <summary>
    /// Выполняет операции синхронизации.
    /// </summary>
    private async Task ExecuteOperationsAsync(
        IReadOnlyList<SyncOperation> operations,
        DirectorySpace sourceDirectory,
        SyncSettings settings,
        SyncContext context,
        CancellationToken cancellationToken)
    {
        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _progressReporter.StartOperation(operation);

            try
            {
                var success = await ExecuteOperationAsync(operation, sourceDirectory, settings, cancellationToken);
                ProcessOperationResult(operation, success, context);
                _progressReporter.CompleteOperation(operation, success);
            }
            catch (Exception exception)
            {
                context.FailedOperations++;
                context.FailedOperationDetails.Add((operation, exception.Message));
                _progressReporter.CompleteOperation(operation, false);
            }
        }
    }

    /// <summary>
    /// Завершает контекст синхронизации.
    /// </summary>
    private void FinalizeSyncContext(SyncContext context)
    {
        context.Stopwatch.Stop();
        context.EndTime = DateTime.Now;
        _progressReporter.Complete();
    }
}
