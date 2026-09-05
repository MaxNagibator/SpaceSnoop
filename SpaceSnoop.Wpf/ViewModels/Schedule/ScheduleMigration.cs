using KeepShell.Services;

namespace SpaceSnoop.Wpf.ViewModels.Schedule;

internal static class ScheduleMigration
{
    internal static SyncProfile? BuildLegacyProfile(ISettingsStore settings)
    {
        var left = (settings.GetStringValue(SettingsKeys.SyncLeft) ?? string.Empty).Trim();
        var right = (settings.GetStringValue(SettingsKeys.SyncRight) ?? string.Empty).Trim();
        var legacyExists = SyncScheduler.Exists(SyncScheduler.LegacyTaskName);

        if (!legacyExists && (left.Length == 0 || right.Length == 0))
        {
            return null;
        }

        var exclusions = (settings.GetStringValue(SettingsKeys.SyncExclusions) ?? string.Empty).Trim();

        if (exclusions.Length == 0)
        {
            exclusions = (settings.GetStringValue(SettingsKeys.DefaultExclusions) ?? string.Empty).Trim();
        }

        return new()
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = "По умолчанию",
            Left = left,
            Right = right,
            Mode = settings.GetInt(SettingsKeys.SyncMode),
            Mirror = settings.GetBool(SettingsKeys.SyncMirror),
            Winner = SyncProfile.WinnerFromIndex(settings.GetInt(SettingsKeys.SyncWinner)),
            Exclusions = exclusions,
            Enabled = legacyExists,
        };
    }

    internal static void ReplaceLegacyTask(SyncProfile model)
    {
        SyncScheduler.Remove(SyncScheduler.LegacyTaskName, out _);
        SyncScheduler.Create(SyncScheduler.TaskNameFor(model.Id), ScheduleInterval.Daily, new(3, 0, 0), $"{AppInfo.SyncArgument} {model.Id}", out _);
    }
}
