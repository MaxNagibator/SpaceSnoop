using System.Globalization;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class ScheduleReconciler
{
    public static int Reconcile(ISettingsStore settings, ILogger logger)
    {
        var exe = Environment.ProcessPath;

        if (string.IsNullOrEmpty(exe))
        {
            return 0;
        }

        var reconciled = 0;

        foreach (var profile in SyncProfileStore.Load(settings))
        {
            if (!profile.Enabled)
            {
                continue;
            }

            var taskName = SyncScheduler.TaskNameFor(profile.Id);
            var status = SyncScheduler.Query(taskName);

            if (!status.Exists || !SyncScheduler.IsStale(status.Action, exe))
            {
                continue;
            }

            TimeSpan.TryParse(profile.Time, CultureInfo.InvariantCulture, out var time);

            if (!SyncScheduler.Create(taskName, profile.Interval, time, $"{AppInfo.SyncArgument} {profile.Id}", out var error))
            {
                logger.ScheduleReconcileFailed(profile.Name, error);
                continue;
            }

            if (!status.Enabled)
            {
                SyncScheduler.Disable(taskName, out _);
            }

            logger.ScheduleTaskReconciled(profile.Name, exe);
            reconciled++;
        }

        return reconciled;
    }
}
