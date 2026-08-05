using System.IO;
using System.Security;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceSnoop.Wpf.Mcp;

internal static class McpFormat
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    public static TimeSpan SnapshotAge(PerformanceSnapshot snapshot)
    {
        return snapshot.CapturedAtUtc == DateTime.MinValue
            ? TimeSpan.Zero
            : DateTime.UtcNow - snapshot.CapturedAtUtc;
    }

    public static string DescribeWindow(PerformanceSnapshot snapshot)
    {
        return snapshot.SampleCount == 0
            ? "замеров ещё нет"
            : $"последние {snapshot.ObservedSpanSeconds:N1} с ({Plural.Format(snapshot.SampleCount, "замер", "замера", "замеров")}), история – отдельно по historySeconds";
    }

    public static McpPerformanceHistory DescribeHistory(PerformanceHistory history, int requestedSeconds, int requestedPoints)
    {
        var timeline = history.Points
            .Select(static point => new McpPerformancePoint(Math.Round(point.AgeMs, 1),
                Math.Round(point.UiDelayMs, 1),
                point.ManagedBytes,
                point.WorkingSetBytes,
                point.Gen0Collections,
                point.Gen1Collections,
                point.Gen2Collections,
                point.Operation))
            .ToList();

        return new(history.CapturedAtUtc,
            Math.Round(history.SpanSeconds, 1),
            requestedSeconds,
            requestedPoints,
            timeline.Count,
            history.Folded,
            history.Gen0Collections,
            history.Gen1Collections,
            history.Gen2Collections,
            timeline);
    }

    public static McpPerformanceOperation? DescribeOperation(PerformanceOperation? operation)
    {
        if (operation is null)
        {
            return null;
        }

        return new(operation.Name,
            operation.Items,
            operation.Bytes,
            SizeFormatter.Format(operation.Bytes),
            Math.Round(operation.Elapsed.TotalSeconds, 1),
            operation.ItemsPerSecond is { } items ? Math.Round(items, 1) : null,
            operation.BytesPerSecond is { } bytes ? Math.Round(bytes, 1) : null,
            operation.Remaining() is { } remaining ? Math.Round(remaining.TotalSeconds, 1) : null,
            PerformanceFormat.Operation(operation) ?? operation.Name);
    }

    public static McpDrive DescribeDrive(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady)
            {
                return new(drive.Name, string.Empty, drive.DriveType.ToString(), null, false, 0, 0, 0,
                    "–", "–", "–", "диск не готов");
            }

            var total = drive.TotalSize;
            var free = drive.TotalFreeSpace;
            var used = Math.Max(0, total - free);

            return new(drive.Name,
                drive.VolumeLabel,
                drive.DriveType.ToString(),
                drive.DriveFormat,
                true,
                total,
                free,
                used,
                SizeFormatter.Format(total),
                SizeFormatter.Format(free),
                SizeFormatter.Format(used),
                null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return new(drive.Name, string.Empty, drive.DriveType.ToString(), null, false, 0, 0, 0,
                "–", "–", "–", exception.Message);
        }
    }

    public static string DescribeVerifyState(SyncVerifyState state, int errorCount)
    {
        return state switch
        {
            SyncVerifyState.Completed when errorCount > 0 =>
                "проверка прошла до конца, но идёт она только по применённым действиям: пути из errors не проверялись, там расхождение осталось",
            SyncVerifyState.Completed => "проверка прошла до конца: пустой mismatches означает, что каталоги сошлись",
            SyncVerifyState.Interrupted => "проверка прервана: часть путей не проверена, пустой mismatches ничего не доказывает",
            _ => "проверка выключена настройкой: сходимость не проверялась",
        };
    }

    public static string DescribePage(string? sectionKey)
    {
        return sectionKey switch
        {
            SectionKey.Scan => "Сканирование",
            SectionKey.Sync => "Синхронизация",
            SectionKey.Overview => "Обзор",
            SectionKey.Schedule => "Расписание",
            SectionKey.Cleanup => "Очистка",
            SectionKey.Docker => "Очистка (Docker)",
            SectionKey.Chat => "Чат",
            SectionKey.Logs => "Логи",
            SectionKey.Performance => "Производительность",
            SectionKey.About => "О программе",
            SectionKey.Settings => "Настройки",
            _ => "неизвестно",
        };
    }

    public static string? DescribeUnavailable(SyncProfile profile)
    {
        return OverviewPipeline.Classify(profile) switch
        {
            OverviewRunStatus.Unavailable => "каталог недоступен или не задан",
            OverviewRunStatus.Overlap => "каталоги совпадают или вложены",
            _ => null,
        };
    }

    public static string? DescribeDeferredNavigation(bool deferred)
    {
        return deferred
            ? "Страница подготовлена, но не открыта: идёт разговор в чате. Переход человек сделает кнопкой в ленте – пересказывать путь словами всё равно нужно."
            : null;
    }
}
