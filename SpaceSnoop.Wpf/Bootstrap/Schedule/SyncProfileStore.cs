using System.Text.Json;

namespace SpaceSnoop.Wpf.Bootstrap.Schedule;

public static class SyncProfileStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static List<SyncProfile> Load(ISettingsStore settings)
    {
        var json = settings.GetStringValue(SettingsKeys.ScheduleProfiles);

        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<SyncProfile>>(json, Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static void Save(ISettingsStore settings, IEnumerable<SyncProfile> profiles)
    {
        settings.SetValue(SettingsKeys.ScheduleProfiles, JsonSerializer.Serialize(profiles, Options));
    }

    public static SyncProfile? Find(ISettingsStore settings, string id)
    {
        return Load(settings).Find(profile => string.Equals(profile.Id, id, StringComparison.Ordinal));
    }
}
