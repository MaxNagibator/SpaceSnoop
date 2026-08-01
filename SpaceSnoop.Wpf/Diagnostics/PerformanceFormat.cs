namespace SpaceSnoop.Wpf.Diagnostics;

public static class PerformanceFormat
{
    public static string Summary(PerformanceSnapshot snapshot)
    {
        var parts = new List<string>(3)
        {
            $"{Math.Round(snapshot.UiDelayMs):N0} мс",
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

        var parts = new List<string>(4) { operation.Name };

        if (operation.ItemsPerSecond is { } items)
        {
            parts.Add($"{items:N0} шт/с");
        }

        if (operation.BytesPerSecond is { } bytes)
        {
            parts.Add($"{SizeFormatter.Format((long)bytes)}/с");
        }

        if (operation.Remaining() is { } remaining)
        {
            parts.Add($"осталось {Duration(remaining)}");
        }

        return string.Join(" · ", parts);
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
