using KeepShell.Diagnostics;

namespace SpaceSnoop.Wpf.Diagnostics;

public static class PerformanceText
{
    public static string Summary(PerformanceSnapshot snapshot, PerformanceOperation? current)
    {
        var parts = new List<string>(3)
        {
            snapshot.UiPeakMs >= AppDefaults.PerformanceHitchMs
                ? $"Отклик {Math.Round(snapshot.UiDelayMs):N0} мс, пик {Math.Round(snapshot.UiPeakMs):N0} мс"
                : $"Отклик {Math.Round(snapshot.UiDelayMs):N0} мс",
            $"Память {SizeFormatter.Format(snapshot.ManagedBytes)}",
        };

        if (Operation(current) is { } operation)
        {
            parts.Add(operation);
        }

        return string.Join(" · ", parts);
    }

    public static string Delay(double lastMs, double peakMs)
    {
        return $"Отклик {Math.Round(lastMs):N0} мс · пик {Math.Round(peakMs):N0} мс";
    }

    public static string Memory(long managedBytes, long workingSetBytes)
    {
        return $"Память {SizeFormatter.Format(managedBytes)} · процесс {SizeFormatter.Format(workingSetBytes)}";
    }

    public static string Collections(int gen0, int gen1, int gen2)
    {
        return $"Сборок мусора {gen0} / {gen1} / {gen2}";
    }

    public static PerformanceOperationTile TileOperation(PerformanceOperation? current, PerformanceOperation? last)
    {
        if (current is { } running)
        {
            return new(
                $"Сейчас идёт: {running.Name}",
                Elapsed(running.Elapsed),
                Volume(running),
                RunningRate(running),
                Traversal(running),
                TraversalDetail(running));
        }

        if (last is { } finished)
        {
            return new(
                $"Последний прогон: {finished.Name}",
                Elapsed(finished.Elapsed),
                Volume(finished),
                Rate(finished) ?? "прогон слишком короткий",
                Traversal(finished),
                TraversalDetail(finished));
        }

        return new(
            "Последний прогон",
            "нет прогонов",
            "сканирование и синхронизация ещё не запускались",
            "после прогона здесь будут объём и скорость");
    }

    public static string? Operation(PerformanceOperation? operation)
    {
        if (operation is null)
        {
            return null;
        }

        var parts = new List<string>(3)
        {
            operation.Name,
        };

        if (Rate(operation) is { } rate)
        {
            parts.Add(rate);
        }

        if (operation.Remaining() is { } remaining)
        {
            parts.Add($"осталось {Duration(remaining)}");
        }

        return string.Join(" · ", parts);
    }

    public static string? Rate(PerformanceOperation? operation)
    {
        if (operation is null)
        {
            return null;
        }

        var parts = new List<string>(2);

        if (operation.ItemsPerSecond is { } items)
        {
            parts.Add($"{items:N0} {Plural.Word((long)items, "файл", "файла", "файлов")}/с");
        }

        if (!operation.LogicalBytes && operation.BytesPerSecond is { } bytes)
        {
            parts.Add($"{SizeFormatter.Format((long)bytes)}/с");
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    public static string? Traversal(PerformanceOperation? operation)
    {
        if (operation?.Traversal is not { } traversal)
        {
            return null;
        }

        var parts = new List<string>(2)
        {
            Directories(traversal.Directories),
        };

        if (operation.DirectoriesPerSecond is { } rate)
        {
            parts.Add($"{rate:N0} {Plural.Word((long)rate, "каталог", "каталога", "каталогов")}/с");
        }

        return string.Join(" · ", parts);
    }

    public static string? TraversalDetail(PerformanceOperation? operation)
    {
        if (operation?.Traversal is not { } traversal)
        {
            return null;
        }

        var parts = new List<string>(3)
        {
            Plural.Format(traversal.Parallelism, "поток", "потока", "потоков"),
        };

        if (traversal.FailedDirectories > 0)
        {
            parts.Add($"{Directories(traversal.FailedDirectories)} без доступа");
        }

        if (operation.Notes is { HasDrops: true } notes
            && ScanDropNote.Describe(notes.DroppedObjects, notes.UnknownSizeFiles, notes.PartialRecords) is { } drops)
        {
            parts.Add($"неполных в $MFT: {drops}");
        }

        if (parts.Count == 1)
        {
            parts.Add("пропусков нет");
        }

        return string.Join(" · ", parts);
    }

    public static string? Remaining(PerformanceOperation? operation)
    {
        return operation?.Remaining() is { } remaining ? $"≈ {Duration(remaining)}" : null;
    }

    public static string Elapsed(TimeSpan value)
    {
        if (value.TotalSeconds < 0.05)
        {
            return "< 0,1 с";
        }

        return value.TotalSeconds < 60
            ? $"{value.TotalSeconds:F1} с"
            : $"{(int)value.TotalMinutes}:{value.Seconds:D2}";
    }

    public static string Duration(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes}:{value.Seconds:00}";
    }

    private static string Directories(long count)
    {
        return $"{count:N0} {Plural.Word(count, "каталог", "каталога", "каталогов")}";
    }

    private static string Volume(PerformanceOperation operation)
    {
        var parts = new List<string>(2);

        if (operation.Items > 0)
        {
            parts.Add($"{operation.Items:N0} {Plural.Word(operation.Items, "файл", "файла", "файлов")}");
        }

        if (operation.Bytes > 0)
        {
            parts.Add(SizeFormatter.Format(operation.Bytes));
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : "не докладывался";
    }

    private static string RunningRate(PerformanceOperation operation)
    {
        var parts = new List<string>(2);

        if (Rate(operation) is { } rate)
        {
            parts.Add(rate);
        }

        if (Remaining(operation) is { } remaining)
        {
            parts.Add($"осталось {remaining}");
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : "рано мерить";
    }
}
