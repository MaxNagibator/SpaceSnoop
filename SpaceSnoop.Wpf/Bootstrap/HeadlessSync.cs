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
            exclusions = profile.Exclusions;
        }
        else
        {
            name = "глобальные настройки";
            left = (settings.GetStringValue(SettingsKeys.SyncLeft) ?? string.Empty).Trim();
            right = (settings.GetStringValue(SettingsKeys.SyncRight) ?? string.Empty).Trim();
            mode = MapMode(settings.GetInt(SettingsKeys.SyncMode));
            mirror = settings.GetBool(SettingsKeys.SyncMirror);
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

        if (!Directory.Exists(left) || !Directory.Exists(right))
        {
            logger.HeadlessSyncAborted("каталог недоступен");
            return 3;
        }

        if (SyncProfile.PathsOverlap(left, right))
        {
            logger.HeadlessSyncAborted("каталоги совпадают или вложены");
            return 5;
        }

        if (mirror && mode != SyncMode.Bidirectional)
        {
            var source = mode == SyncMode.RightToLeft ? right : left;

            if (!Directory.EnumerateFileSystemEntries(source).Any())
            {
                logger.HeadlessSyncAborted("зеркало отменено: источник пуст");
                return 4;
            }
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

            result.ApplyMode(mode, mirror);
            result.ResolveAllConflicts(SyncAction.Skip);

            var engine = new SyncEngine(NullLogger<SyncEngine>.Instance);
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
            var path = Path.Combine(AppStorage.DataDirectory, AppInfo.SyncLogFileName);
            using var writer = new StreamWriter(path, true);
            writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Автосинхронизация [{name}]: {report.SuccessCount} успешно, {report.Errors.Count} ошибок");

            foreach (var error in report.Errors)
            {
                writer.WriteLine($"  ОШИБКА: {error.RelativePath} ({error.Action}): {error.Message}");
            }
        }
        catch (Exception exception)
        {
            logger.SyncLogWriteFailed(exception);
        }
    }
}
