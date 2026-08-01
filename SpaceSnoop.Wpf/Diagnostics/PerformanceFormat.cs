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

    public static string ChartDelay(PerformanceChartData data)
    {
        return $"Отклик, пик {Math.Round(data.PeakDelayMs):N0} мс";
    }

    public static string ChartMemory(PerformanceChartData data)
    {
        return data.MemoryMaxBytes > data.MemoryMinBytes
            ? $"Память {SizeFormatter.Format(data.MemoryMinBytes)} – {SizeFormatter.Format(data.MemoryMaxBytes)}"
            : $"Память {SizeFormatter.Format(data.MemoryMaxBytes)}";
    }

    public static string ChartWindow(PerformanceChartData data)
    {
        return $"за {Duration(TimeSpan.FromSeconds(data.SpanSeconds))} · {Plural.Format(data.Delay.Count, "замер", "замера", "замеров")}";
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
}
