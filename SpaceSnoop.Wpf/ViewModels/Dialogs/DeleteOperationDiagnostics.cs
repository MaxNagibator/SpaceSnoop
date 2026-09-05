using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

internal sealed class DeleteOperationDiagnostics
{
    private readonly bool _permanent;
    private readonly int _totalItems;
    private readonly long _totalBytes;
    private readonly PerformanceMonitor _performance;
    private readonly PerformanceRunTracker _runs;
    private readonly ShellPreferences _preferences;

    private PerformanceOperation? _reported;
    private long _started;
    private long _lastDeleteTicks;
    private long _maximumDeleteTicks;
    private long _totalDeleteTicks;
    private int _deleteCalls;
    private int _fallbackCalls;
    private long _totalExistsTicks;
    private int _existsCalls;

    public DeleteOperationDiagnostics(
        bool permanent,
        int totalItems,
        long totalBytes,
        PerformanceMonitor performance,
        PerformanceRunTracker runs,
        ShellPreferences preferences)
    {
        _permanent = permanent;
        _totalItems = totalItems;
        _totalBytes = totalBytes;
        _performance = performance;
        _runs = runs;
        _preferences = preferences;
    }

    public void Start()
    {
        _performance.Start();
        _started = Stopwatch.GetTimestamp();
    }

    public void Finish(int processed, long freed, bool succeeded)
    {
        if (_started == 0)
        {
            return;
        }

        if (succeeded)
        {
            _runs.Report(DescribeOperation(processed, freed, Stopwatch.GetElapsedTime(_started), includeTotals: false));
        }

        _performance.ClearOperation(_reported);
        _reported = null;
        _started = 0;
    }

    public string? Report(int processed, long freed)
    {
        if (_started == 0)
        {
            return null;
        }

        var operation = DescribeOperation(processed, freed, Stopwatch.GetElapsedTime(_started), includeTotals: true);

        if (_performance.TryReportOperation(operation, _reported))
        {
            _reported = operation;
        }

        return _preferences.ShowPerformanceHud ? Describe(operation) : null;
    }

    public DeleteBatchCallbacks BuildCallbacks()
    {
        return _permanent
            ? new(TimedPathExists, TimedDeletePermanentChunk, TimedDeletePermanent)
            : new(TimedPathExists, TimedRecycleChunk, TimedRecycleSingle);
    }

    private PerformanceOperation DescribeOperation(int processed, long freed, TimeSpan elapsed, bool includeTotals)
    {
        return new(
            "Удаление",
            processed,
            freed,
            elapsed,
            includeTotals ? _totalItems : null,
            includeTotals ? _totalBytes : null,
            includeTotals ? EtaBasis.Items : EtaBasis.None,
            LogicalBytes: true);
    }

    private string Describe(PerformanceOperation operation)
    {
        var snapshot = _performance.Snapshot;
        var parts = new List<string>(5)
        {
            snapshot.SampleCount > 0
                ? PerformanceFormat.Delay(snapshot.UiDelayMs, snapshot.UiPeakMs)
                : "отклик ещё не измерен",
            PerformanceFormat.Rate(operation) ?? "скорость считается",
        };

        if (operation.Remaining() is { } remaining)
        {
            parts.Add($"осталось {PerformanceFormat.Duration(remaining)}");
        }

        var deleteCalls = Volatile.Read(ref _deleteCalls);

        if (deleteCalls > 0)
        {
            parts.Add(
                $"операций удаления {deleteCalls:N0} · последний {FormatMilliseconds(Volatile.Read(ref _lastDeleteTicks))} мс"
                + $" · максимум {FormatMilliseconds(Volatile.Read(ref _maximumDeleteTicks))} мс"
                + $" · всего {FormatMilliseconds(Interlocked.Read(ref _totalDeleteTicks))} мс");
        }

        var existsCalls = Volatile.Read(ref _existsCalls);

        if (existsCalls > 0)
        {
            parts.Add($"проверка путей {existsCalls:N0} · {FormatMilliseconds(Interlocked.Read(ref _totalExistsTicks))} мс");
        }

        var fallback = Volatile.Read(ref _fallbackCalls);

        if (fallback > 0)
        {
            parts.Add($"повторено по одному: {fallback:N0}");
        }

        return string.Join(" · ", parts);
    }


    private static bool PathExists(string path)
    {
        return Directory.Exists(path) || File.Exists(path);
    }

    private static void DeletePermanent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
            return;
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Путь не найден", path);
        }

        File.Delete(path);
    }

    private static void DeletePermanentChunk(IReadOnlyList<string> chunk)
    {
        foreach (var path in chunk)
        {
            DeletePermanent(path);
        }
    }

    private bool TimedPathExists(string path)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            return PathExists(path);
        }
        finally
        {
            Interlocked.Increment(ref _existsCalls);
            Interlocked.Add(ref _totalExistsTicks, Stopwatch.GetTimestamp() - started);
        }
    }

    private void TimedRecycleChunk(IReadOnlyList<string> paths)
    {
        MeasureDelete(RecycleBin.DeleteSilent, paths);
    }

    private void TimedRecycleSingle(string path)
    {
        Interlocked.Increment(ref _fallbackCalls);
        MeasureDelete(RecycleBin.DeleteSilent, path);
    }

    private void TimedDeletePermanentChunk(IReadOnlyList<string> paths)
    {
        MeasureDelete(DeletePermanentChunk, paths);
    }

    private void TimedDeletePermanent(string path)
    {
        Interlocked.Increment(ref _fallbackCalls);
        MeasureDelete(DeletePermanent, path);
    }

    private void MeasureDelete<T>(Action<T> action, T argument)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            action(argument);
        }
        finally
        {
            var elapsed = Stopwatch.GetTimestamp() - started;

            Interlocked.Add(ref _totalDeleteTicks, elapsed);
            Volatile.Write(ref _lastDeleteTicks, elapsed);
            UpdateMaximum(ref _maximumDeleteTicks, elapsed);
            Interlocked.Increment(ref _deleteCalls);
        }
    }

    private static void UpdateMaximum(ref long target, long value)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);

            if (value <= current || Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }

    private static string FormatMilliseconds(long stopwatchTicks)
    {
        return Stopwatch.GetElapsedTime(0, stopwatchTicks).TotalMilliseconds.ToString("N0");
    }
}
