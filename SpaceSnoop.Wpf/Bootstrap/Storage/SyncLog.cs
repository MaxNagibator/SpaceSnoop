using Serilog;
using System.IO;
using System.Text;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace SpaceSnoop.Wpf.Bootstrap.Storage;

public enum SyncLogOrigin
{
    None = 0,
    Scheduled = 1,
    Overview = 2,
    Manual = 3,
    Mcp = 4,
}

public static class SyncLog
{
    public const string FileGlob = "sync-log*.txt";

    private const string NoErrors = ", 0 ошибок";

    private const string NoMismatches = $"{NoErrors}, расхождений: 0";

    public static string FilePath => Path.Combine(AppStorage.DataDirectory, AppInfo.SyncLogFileName);

    internal static void AppendSafe(SyncLogOrigin origin, string? name, SyncReport report, SyncVerifyState verify, ILogger logger)
    {
        try
        {
            Append(FilePath, FormatHeader(origin, name, report, verify, DateTime.Now), report);
        }
        catch (Exception exception)
        {
            logger.SyncLogWriteFailed(exception);
        }
    }

    internal static string FormatHeader(SyncLogOrigin origin, string? name, SyncReport report, SyncVerifyState verify, DateTime timestamp)
    {
        var scope = string.IsNullOrEmpty(name) ? string.Empty : $" [{name}]";
        var outcome = SyncPlanNarrative.DescribeVerify(verify, report.Mismatches.Count);
        return $"[{timestamp:yyyy-MM-dd HH:mm:ss}] {Marker(origin)}{scope}: {report.SuccessCount} успешно, {report.Errors.Count} ошибок{outcome}";
    }

    internal static bool MatchesOrigin(string line, SyncLogOrigin origin)
    {
        if (Marker(origin) is not { Length: > 0 } marker || !line.StartsWith('['))
        {
            return false;
        }

        var start = line.IndexOf("] ", StringComparison.Ordinal);

        if (start < 0)
        {
            return false;
        }

        var head = line.AsSpan(start + 2);

        return head.StartsWith(marker, StringComparison.Ordinal)
               && (origin != SyncLogOrigin.Manual || !head.StartsWith(Marker(SyncLogOrigin.Mcp), StringComparison.Ordinal));
    }

    internal static bool LineHasErrors(string line)
    {
        return !line.EndsWith(NoErrors, StringComparison.Ordinal)
               && !line.EndsWith(NoMismatches, StringComparison.Ordinal);
    }

    private static string Marker(SyncLogOrigin origin)
    {
        return origin switch
        {
            SyncLogOrigin.Scheduled => "Автосинхронизация",
            SyncLogOrigin.Overview => "Обзор",
            SyncLogOrigin.Mcp => "Синхронизация (запуск агентом через MCP)",
            SyncLogOrigin.Manual => "Синхронизация",
            _ => string.Empty,
        };
    }

    internal static void Append(string filePath, string header, SyncReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine(header);

        using (var writer = new StringWriter(builder))
        {
            report.WriteDetails(writer);
        }

        using var logger = new LoggerConfiguration()
            .WriteTo.File(
                filePath,
                outputTemplate: "{Message:l}{NewLine}",
                fileSizeLimitBytes: AppDefaults.SyncLogFileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: AppDefaults.SyncLogRetainedFileCount)
            .CreateLogger();

        logger.Information("{Entry}", builder.ToString().TrimEnd());
    }

    public static IReadOnlyList<string> ReadTail(Func<string, bool> match, int count, Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        return ReadTail(AppStorage.DataDirectory, match, count, logger);
    }

    internal static IReadOnlyList<string> ReadTail(string directory, Func<string, bool> match, int count, Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        var files = Directory
            .EnumerateFiles(directory, FileGlob)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal);

        var tail = new Queue<string>(count);

        foreach (var file in files)
        {
            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (!match(line))
                    {
                        continue;
                    }

                    tail.Enqueue(line);

                    if (tail.Count > count)
                    {
                        tail.Dequeue();
                    }
                }
            }
            catch (IOException exception)
            {
                logger?.SyncLogFileUnreadable(exception, file);
            }
        }

        return tail.Reverse().ToList();
    }
}
