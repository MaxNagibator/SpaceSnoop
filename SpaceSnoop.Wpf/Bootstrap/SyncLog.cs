using Serilog;
using System.IO;
using System.Text;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class SyncLog
{
    public const string FileGlob = "sync-log*.txt";

    public static string FilePath => Path.Combine(AppStorage.DataDirectory, AppInfo.SyncLogFileName);

    public static void Append(string header, SyncReport report)
    {
        Append(FilePath, header, report);
    }

    internal static void AppendSafe(string header, SyncReport report, ILogger logger)
    {
        try
        {
            Append(header, report);
        }
        catch (Exception exception)
        {
            logger.SyncLogWriteFailed(exception);
        }
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
