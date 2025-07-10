using System.Diagnostics;

namespace SpaceSnoop.Services.Sync;

/// <summary>
/// Репортер прогресса синхронизации.
/// </summary>
public class SyncProgressReporter
{
    private readonly Stopwatch _stopwatch;
    private readonly Lock _lock = new();

    private int _totalOperations;
    private int _completedOperations;
    private long _totalBytes;
    private long _processedBytes;
    private SyncOperation? _currentOperation;

    /// <summary>
    /// Создает новый экземпляр репортера прогресса.
    /// </summary>
    public SyncProgressReporter()
    {
        _stopwatch = new();
    }

    /// <summary>
    /// Событие изменения прогресса синхронизации.
    /// </summary>
    public event EventHandler<SyncProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Общее количество операций.
    /// </summary>
    public int TotalOperations
    {
        get
        {
            lock (_lock)
            {
                return _totalOperations;
            }
        }
    }

    /// <summary>
    /// Количество завершенных операций.
    /// </summary>
    public int CompletedOperations
    {
        get
        {
            lock (_lock)
            {
                return _completedOperations;
            }
        }
    }

    /// <summary>
    /// Общее количество байт для обработки.
    /// </summary>
    public long TotalBytes
    {
        get
        {
            lock (_lock)
            {
                return _totalBytes;
            }
        }
    }

    /// <summary>
    /// Количество обработанных байт.
    /// </summary>
    public long ProcessedBytes
    {
        get
        {
            lock (_lock)
            {
                return _processedBytes;
            }
        }
    }

    /// <summary>
    /// Текущая выполняемая операция.
    /// </summary>
    public SyncOperation? CurrentOperation
    {
        get
        {
            lock (_lock)
            {
                return _currentOperation;
            }
        }
    }

    /// <summary>
    /// Прошедшее время с начала синхронизации.
    /// </summary>
    public TimeSpan ElapsedTime => _stopwatch.Elapsed;

    /// <summary>
    /// Процент завершения по количеству операций.
    /// </summary>
    public double OperationProgress
    {
        get
        {
            lock (_lock)
            {
                return _totalOperations > 0 ? (double)_completedOperations / _totalOperations * 100 : 0;
            }
        }
    }

    /// <summary>
    /// Процент завершения по количеству байт.
    /// </summary>
    public double ByteProgress
    {
        get
        {
            lock (_lock)
            {
                return _totalBytes > 0 ? (double)_processedBytes / _totalBytes * 100 : 0;
            }
        }
    }

    /// <summary>
    /// Инициализирует репортер с общим количеством операций и байт.
    /// </summary>
    /// <param name="operations">Список операций для выполнения.</param>
    public void Initialize(IReadOnlyList<SyncOperation> operations)
    {
        lock (_lock)
        {
            _totalOperations = operations.Count;
            _completedOperations = 0;
            _totalBytes = operations.Where(op => op.FileSize.HasValue).Sum(op => op.FileSize!.Value);
            _processedBytes = 0;
            _currentOperation = null;
        }

        _stopwatch.Restart();
        ReportProgress();
    }

    /// <summary>
    /// Отмечает начало выполнения операции.
    /// </summary>
    /// <param name="operation">Операция, которая начинается.</param>
    public void StartOperation(SyncOperation operation)
    {
        lock (_lock)
        {
            _currentOperation = operation;
        }

        ReportProgress();
    }

    /// <summary>
    /// Отмечает завершение операции.
    /// </summary>
    /// <param name="operation">Завершенная операция.</param>
    /// <param name="success">Успешно ли завершена операция.</param>
    public void CompleteOperation(SyncOperation operation, bool success)
    {
        lock (_lock)
        {
            _completedOperations++;

            if (success && operation.FileSize.HasValue)
            {
                _processedBytes += operation.FileSize.Value;
            }

            _currentOperation = null;
        }

        ReportProgress();
    }

    /// <summary>
    /// Обновляет прогресс обработки байт для текущей операции.
    /// </summary>
    /// <param name="processedBytes">Количество обработанных байт в текущей операции.</param>
    public void UpdateBytesProgress(long processedBytes)
    {
        lock (_lock)
        {
            // Обновляем только если есть текущая операция с размером файла
            if (_currentOperation?.FileSize.HasValue == true)
            {
                var operationBytes = Math.Min(processedBytes, _currentOperation.FileSize.Value);
                // Здесь можно добавить более сложную логику для отслеживания прогресса внутри операции
            }
        }

        ReportProgress();
    }

    /// <summary>
    /// Завершает отслеживание прогресса.
    /// </summary>
    public void Complete()
    {
        _stopwatch.Stop();

        lock (_lock)
        {
            _currentOperation = null;
        }

        ReportProgress();
    }

    /// <summary>
    /// Сбрасывает состояние репортера.
    /// </summary>
    public void Reset()
    {
        _stopwatch.Reset();

        lock (_lock)
        {
            _totalOperations = 0;
            _completedOperations = 0;
            _totalBytes = 0;
            _processedBytes = 0;
            _currentOperation = null;
        }
    }

    /// <summary>
    /// Вычисляет оценочное время до завершения.
    /// </summary>
    /// <returns>Оценочное время до завершения или null, если невозможно вычислить.</returns>
    public TimeSpan? CalculateEstimatedTimeRemaining()
    {
        lock (_lock)
        {
            if (_completedOperations == 0 || _totalOperations == 0)
            {
                return null;
            }

            var progress = (double)_completedOperations / _totalOperations;

            if (progress <= 0)
            {
                return null;
            }

            var elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
            var estimatedTotalSeconds = elapsedSeconds / progress;
            var remainingSeconds = estimatedTotalSeconds - elapsedSeconds;

            return remainingSeconds > 0 ? TimeSpan.FromSeconds(remainingSeconds) : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Отправляет событие изменения прогресса.
    /// </summary>
    private void ReportProgress()
    {
        SyncProgressEventArgs args;

        lock (_lock)
        {
            var estimatedTimeRemaining = CalculateEstimatedTimeRemaining();

            args = new(_currentOperation,
                _completedOperations,
                _totalOperations,
                _processedBytes,
                _totalBytes,
                _stopwatch.Elapsed,
                estimatedTimeRemaining);
        }

        ProgressChanged?.Invoke(this, args);
    }
}
