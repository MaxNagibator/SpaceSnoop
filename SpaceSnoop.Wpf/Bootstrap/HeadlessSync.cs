using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.Bootstrap;

internal sealed class HeadlessSync
{
    public static int Run(ISettingsStore settings, KeepShellLogging logging, string? profileId = null)
    {
        var logger = logging.CreateLogger<HeadlessSync>();

        string name, left, right, exclusions;
        SyncMode mode;
        bool mirror;
        SyncWinner winner;

        if (!string.IsNullOrEmpty(profileId))
        {
            var profile = SyncProfileStore.Find(settings, profileId);

            if (profile is null)
            {
                logger.HeadlessSyncAborted($"профиль не найден: {profileId}");
                return 2;
            }

            name = profile.Name;
            left = profile.Left.Trim();
            right = profile.Right.Trim();
            mode = MapMode(profile.Mode);
            mirror = profile.Mirror;
            winner = profile.Winner;
            exclusions = profile.Exclusions;
        }
        else
        {
            name = "глобальные настройки";
            left = (settings.GetStringValue(SettingsKeys.SyncLeft) ?? string.Empty).Trim();
            right = (settings.GetStringValue(SettingsKeys.SyncRight) ?? string.Empty).Trim();
            mode = MapMode(settings.GetInt(SettingsKeys.SyncMode));
            mirror = settings.GetBool(SettingsKeys.SyncMirror);
            winner = SyncProfile.WinnerFromIndex(settings.GetInt(SettingsKeys.SyncWinner));
            exclusions = settings.GetStringValue(SettingsKeys.SyncExclusions) ?? string.Empty;

            if (string.IsNullOrEmpty(exclusions))
            {
                exclusions = settings.GetStringValue(SettingsKeys.DefaultExclusions) ?? string.Empty;
            }
        }

        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            logger.HeadlessSyncAborted("каталоги не настроены");
            return 2;
        }

        if (SyncProfile.SourceMissing(left, right, mode))
        {
            logger.HeadlessSyncAborted("каталог-источник недоступен");
            return 3;
        }

        if (SyncProfile.PathsOverlap(left, right))
        {
            logger.HeadlessSyncAborted("каталоги совпадают или вложены");
            return 5;
        }

        if (mirror && SyncProfile.MirrorSource(mode, winner, left, right) is { } mirrorSource
            && (!Directory.Exists(mirrorSource) || !Directory.EnumerateFileSystemEntries(mirrorSource).Any()))
        {
            logger.HeadlessSyncAborted("зеркало отменено: источник пуст");
            return 4;
        }

        logger.HeadlessSyncStarted(left, right, mode, mirror);
        var stopwatch = Stopwatch.StartNew();

        using var mutex = new Mutex(false, @"Global\SpaceSnoop_HeadlessSync");
        var acquired = false;

        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromMinutes(10));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                logger.HeadlessSyncAborted("другой прогон ещё выполняется");
                return 1;
            }

            var filter = new ExclusionFilter(exclusions);
            var comparer = new DirectoryComparer(filter, NullLogger<DirectoryComparer>.Instance);
            var result = comparer.Compare(left, right, CancellationToken.None);

            result.ApplyMode(mode, mirror, winner);
            result.ResolveAllConflicts(SyncAction.Skip);

            var engine = new SyncEngine(NullLogger<SyncEngine>.Instance, false);
            var report = engine.Execute(result, CancellationToken.None);

            stopwatch.Stop();
            WriteLog(name, report, logger);
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
}
