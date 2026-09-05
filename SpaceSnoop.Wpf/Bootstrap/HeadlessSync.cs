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

            var compare = new CompareDirectoriesUseCase(NullLogger<DirectoryComparer>.Instance);
            var result = compare.Execute(new(options.Left, options.Right, options.Exclusions, options.Mode, options.Winner, options.Mirror), CancellationToken.None);

            if (result.IncompleteDirectories() is { Count: > 0 } unreadable)
            {
                logger.CompareIncomplete(unreadable.Count, unreadable[0]);
            }

            if (result.SkippedLinks() is { Count: > 0 } links)
            {
                logger.CompareLinksSkipped(links.Count, links[0]);
            }

            var sync = new ExecuteSyncUseCase(NullLogger<SyncEngine>.Instance);
            var recycleOverwritten = settings.GetBool(SettingsKeys.SyncRecycleOverwritten, AppDefaults.SyncRecycleOverwrittenDefault);
            var verify = settings.GetBool(SettingsKeys.SyncVerify, AppDefaults.SyncVerifyDefault);
            var report = sync.Execute(new(result, SyncConflictPolicy.SkipUnresolved, SyncDeleteUi.Silent, verify, recycleOverwritten), CancellationToken.None);

            stopwatch.Stop();
            WriteLog(options.Name, report, SyncPlanNarrative.ResolveVerify(verify, report), logger);
            logger.HeadlessSyncFinished(report.SuccessCount, report.Errors.Count, report.Mismatches.Count, (long)stopwatch.Elapsed.TotalMilliseconds);

            return Outcome(report);
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

    internal static int Outcome(SyncReport report)
    {
        if (report.Errors.Count > 0)
        {
            return 1;
        }

        return report.Mismatches.Count > 0 ? 6 : 0;
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

    internal static RunOptions? LoadOptions(ISettingsStore settings, string? profileId, ILogger logger)
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

    internal static int Validate(RunOptions options, ILogger logger)
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

    private static void WriteLog(string name, SyncReport report, SyncVerifyState verify, ILogger logger)
    {
        SyncLog.AppendSafe(SyncLogOrigin.Scheduled, name, report, verify, logger);
    }

    internal sealed record RunOptions(
        string Name,
        string Left,
        string Right,
        SyncMode Mode,
        bool Mirror,
        SyncWinner Winner,
        string Exclusions);
}
