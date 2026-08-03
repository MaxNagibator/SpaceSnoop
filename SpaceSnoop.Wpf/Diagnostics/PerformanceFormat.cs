namespace SpaceSnoop.Wpf.Diagnostics;

public static class PerformanceFormat
{
    public static string Summary(PerformanceSnapshot snapshot)
    {
        var parts = new List<string>(3)
        {
            snapshot.UiPeakMs >= AppDefaults.PerformanceHitchMs
                ? $"{Math.Round(snapshot.UiDelayMs):N0} мс · пик {Math.Round(snapshot.UiPeakMs):N0} мс"
                : $"{Math.Round(snapshot.UiDelayMs):N0} мс",
            SizeFormatter.Format(snapshot.ManagedBytes),
        };

        if (Operation(snapshot.Operation) is { } operation)
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

    public static string TileDelay(PerformanceSnapshot snapshot)
    {
        return snapshot.SampleCount == 0 ? "нет замеров" : $"{Math.Round(snapshot.UiPeakMs):N0} мс";
    }

    public static string TileDelayHint(PerformanceSnapshot snapshot)
    {
        return snapshot.SampleCount == 0
            ? "сбор только запущен или сброшен"
            : $"сейчас {Math.Round(snapshot.UiDelayMs):N0} мс · среднее {Math.Round(snapshot.UiAverageMs):N0} мс";
    }

    public static string TileMemory(PerformanceSnapshot snapshot)
    {
        return SizeFormatter.Format(snapshot.WorkingSetBytes);
    }

    public static string TileMemoryHint(PerformanceSnapshot snapshot)
    {
        return $"управляемой {SizeFormatter.Format(snapshot.ManagedBytes)}";
    }

    public static string TileMemoryPeak(PerformanceSnapshot snapshot)
    {
        return snapshot.WorkingSetPeakBytes > 0
            ? $"пик за сеанс {SizeFormatter.Format(snapshot.WorkingSetPeakBytes)}"
            : "пик за сеанс: замеров ещё нет";
    }

    public static string TileCollections(PerformanceSnapshot snapshot)
    {
        return $"{snapshot.Gen0Collections} / {snapshot.Gen1Collections} / {snapshot.Gen2Collections}";
    }

    public static string TileCollectionsWindow(PerformanceSnapshot snapshot)
    {
        var history = snapshot.History;

        return history.SampleCount < 2
            ? "за окно наблюдения: мерить не по чему"
            : $"за последние {Duration(TimeSpan.FromSeconds(history.SpanSeconds))}: {history.Gen0Collections} / {history.Gen1Collections} / {history.Gen2Collections}";
    }

    public static string TileWindow(PerformanceSnapshot snapshot)
    {
        return snapshot.SampleCount == 0
            ? "окно: замеров ещё нет"
            : $"окно: {Plural.Format(snapshot.SampleCount, "замер", "замера", "замеров")} за {snapshot.ObservedSpanSeconds:N1} с";
    }

    public static string TileStartup(PerformanceSnapshot snapshot)
    {
        return snapshot.StartupSeconds > 0 ? $"{snapshot.StartupSeconds:N2} с" : "не измерялся";
    }

    public static string TileStartupHint(PerformanceSnapshot snapshot)
    {
        return snapshot.StartupSeconds > 0
            ? "от запуска до первого кадра окна"
            : "окно не показывалось, замер не доложен";
    }

    public static PerformanceOperationTile TileOperation(PerformanceOperation? current, PerformanceOperation? last)
    {
        if (current is { } running)
        {
            return new($"Сейчас идёт: {running.Name}", Elapsed(running.Elapsed), Volume(running), RunningRate(running));
        }

        if (last is { } finished)
        {
            return new($"Последний прогон: {finished.Name}",
                Elapsed(finished.Elapsed),
                Volume(finished),
                Rate(finished) ?? "скорость: прогон слишком короткий");
        }

        return new("Последний прогон",
            "нет прогонов",
            "сканирование и синхронизация ещё не запускались",
            "после прогона здесь будут объём и скорость");
    }

    public static string? StaleWarning(PerformanceSnapshot snapshot, DateTime nowUtc)
    {
        if (snapshot.CapturedAtUtc == DateTime.MinValue)
        {
            return null;
        }

        var ageMs = (nowUtc - snapshot.CapturedAtUtc).TotalMilliseconds;

        return ageMs > 2 * AppDefaults.PerformanceSampleIntervalMs
            ? $"Снимок сделан {Ago(ageMs)}: UI-поток не успевает снимать замеры, числа ниже устарели"
            : null;
    }

    public static string ChartVerdict(PerformanceChartData data)
    {
        if (!data.HasData)
        {
            return string.Empty;
        }

        var scale = $"пик {Math.Round(data.PeakDelayMs):N0} мс при пороге {AppDefaults.PerformanceHitchMs} мс";

        if (!data.HasHitches)
        {
            return $"Просадок нет · {scale}";
        }

        var count = Plural.Format(data.Hitches.Count, "просадка", "просадки", "просадок");

        return $"{count} · последняя {Ago(data.Hitches[^1].AgeMs)} · {scale}";
    }

    public static string Age(double seconds)
    {
        if (seconds < 1)
        {
            return "сейчас";
        }

        return seconds < 60
            ? $"−{Math.Round(seconds):N0} с"
            : $"−{Duration(TimeSpan.FromSeconds(seconds))}";
    }

    public static string Ago(double ageMs)
    {
        var seconds = ageMs / 1000;

        return seconds < 60
            ? $"{Math.Round(seconds):N0} с назад"
            : $"{Duration(TimeSpan.FromSeconds(seconds))} назад";
    }

    public static string ChartMemory(PerformanceChartData data)
    {
        return data.MemoryMaxBytes > data.MemoryMinBytes
            ? $"Память {SizeFormatter.Format(data.MemoryMinBytes)} – {SizeFormatter.Format(data.MemoryMaxBytes)}"
            : $"Память {SizeFormatter.Format(data.MemoryMaxBytes)}";
    }

    public static string ChartWindow(PerformanceChartData data)
    {
        return $"за {Duration(TimeSpan.FromSeconds(data.SpanSeconds))} · {Plural.Format(data.Points.Count, "замер", "замера", "замеров")}";
    }

    public static string Collections(int gen0, int gen1, int gen2)
    {
        return $"Сборок мусора {gen0} / {gen1} / {gen2}";
    }

    public static string? Operation(PerformanceOperation? operation)
    {
        if (operation is null)
        {
            return null;
        }

        var parts = new List<string>(3) { operation.Name };

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
            parts.Add($"{items:N0} файл/с");
        }

        if (operation.BytesPerSecond is { } bytes)
        {
            parts.Add($"{SizeFormatter.Format((long)bytes)}/с");
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    public static string? Remaining(PerformanceOperation? operation)
    {
        return operation?.Remaining() is { } remaining ? $"≈ {Duration(remaining)}" : null;
    }

    public static string Elapsed(TimeSpan value)
    {
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

        return parts.Count > 0 ? string.Join(" · ", parts) : "объём не докладывался";
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

        return parts.Count > 0 ? string.Join(" · ", parts) : "скорость: рано мерить";
    }
}
