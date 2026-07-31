using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.Bootstrap;

internal sealed class HeadlessSync
{
    public static int Run(ISettingsStore settings, KeepShellLogging logging, string? profileId = null)
    {
        var logger = logging.CreateLogger<HeadlessSync>();
        var options = LoadOptions(settings, profileId, logger);

        if (options is null)
        {
            return 2;
        }

        var validationCode = Validate(options, logger);

        if (validationCode != 0)
        {
            return validationCode;
        }

        logger.HeadlessSyncStarted(options.Left, options.Right, options.Mode, options.Mirror);
        var stopwatch = Stopwatch.StartNew();

        using var mutex = new Mutex(false, @"Global\SpaceSnoop_HeadlessSync");
        var acquired = false;

        try
        {
            acquired = TryAcquire(mutex);

            if (!acquired)
            {
                logger.HeadlessSyncAborted("другой прогон ещё выполняется");
                return 1;
            }

            var filter = new ExclusionFilter(options.Exclusions);
            var comparer = new DirectoryComparer(filter, NullLogger<DirectoryComparer>.Instance);
            var result = comparer.Compare(options.Left, options.Right, CancellationToken.None);

            result.ApplyMode(options.Mode, options.Mirror, options.Winner);
            result.ResolveAllConflicts(SyncAction.Skip);

            var engine = new SyncEngine(NullLogger<SyncEngine>.Instance, false);
            var report = engine.Execute(result, CancellationToken.None);

            stopwatch.Stop();
            WriteLog(options.Name, report, logger);
            logger.HeadlessSyncFinished(report.SuccessCount, report.Errors.Count, (long)stopwatch.Elapsed.TotalMilliseconds);

            return report.Errors.Count == 0 ? 0 : 1;
        }
        catch (Exception exception)
        {
            logger.HeadlessSyncFailed(exception);
            return 1;
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    internal static SyncMode MapMode(int modeIndex)
    {
        return modeIndex switch
        {
            1 => SyncMode.RightToLeft,
            2 => SyncMode.Bidirectional,
            _ => SyncMode.LeftToRight,
        };
    }

    private static RunOptions? LoadOptions(ISettingsStore settings, string? profileId, ILogger logger)
    {
        if (!string.IsNullOrEmpty(profileId))
        {
            var profile = SyncProfileStore.Find(settings, profileId);

            if (profile is null)
            {
                logger.HeadlessSyncAborted($"профиль не найден: {profileId}");
                return null;
            }

            return new(profile.Name, profile.Left.Trim(), profile.Right.Trim(), MapMode(profile.Mode), profile.Mirror, profile.Winner, profile.Exclusions);
        }

        var exclusions = settings.GetStringValue(SettingsKeys.SyncExclusions) ?? string.Empty;

        if (string.IsNullOrEmpty(exclusions))
        {
            exclusions = settings.GetStringValue(SettingsKeys.DefaultExclusions) ?? string.Empty;
        }

        return new("глобальные настройки",
            (settings.GetStringValue(SettingsKeys.SyncLeft) ?? string.Empty).Trim(),
            (settings.GetStringValue(SettingsKeys.SyncRight) ?? string.Empty).Trim(),
            MapMode(settings.GetInt(SettingsKeys.SyncMode)),
            settings.GetBool(SettingsKeys.SyncMirror),
            SyncProfile.WinnerFromIndex(settings.GetInt(SettingsKeys.SyncWinner)),
            exclusions);
    }

    private static int Validate(RunOptions options, ILogger logger)
    {
        if (string.IsNullOrEmpty(options.Left) || string.IsNullOrEmpty(options.Right))
        {
            logger.HeadlessSyncAborted("каталоги не настроены");
            return 2;
        }

        if (SyncProfile.SourceMissing(options.Left, options.Right, options.Mode))
        {
            logger.HeadlessSyncAborted("каталог-источник недоступен");
            return 3;
        }

        if (SyncProfile.PathsOverlap(options.Left, options.Right))
        {
            logger.HeadlessSyncAborted("каталоги совпадают или вложены");
            return 5;
        }

        if (options.Mirror && IsMirrorSourceEmpty(options))
        {
            logger.HeadlessSyncAborted("зеркало отменено: источник пуст");
            return 4;
        }

        return 0;
    }

    private static bool IsMirrorSourceEmpty(RunOptions options)
    {
        return SyncProfile.MirrorSource(options.Mode, options.Winner, options.Left, options.Right) is { } mirrorSource
               && (!Directory.Exists(mirrorSource) || !Directory.EnumerateFileSystemEntries(mirrorSource).Any());
    }

    private static bool TryAcquire(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(TimeSpan.FromMinutes(10));
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private static void WriteLog(string name, SyncReport report, ILogger logger)
    {
        try
        {
            SyncLog.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Автосинхронизация [{name}]: {report.SuccessCount} успешно, {report.Errors.Count} ошибок", report);
        }
        catch (Exception exception)
        {
            logger.SyncLogWriteFailed(exception);
        }
    }

    private sealed record RunOptions(
        string Name,
        string Left,
        string Right,
        SyncMode Mode,
        bool Mirror,
        SyncWinner Winner,
        string Exclusions);
}
