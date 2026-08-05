namespace SpaceSnoop.Wpf.Bootstrap;

public static class SectionKey
{
    public const string Scan = "scan";
    public const string Sync = "sync";
    public const string Overview = "overview";
    public const string Schedule = "schedule";
    public const string Cleanup = "cleanup";
    public const string Docker = "docker";
    public const string Chat = "chat";
    public const string Logs = "logs";
    public const string Performance = "performance";
    public const string About = "about";
    public const string Settings = "settings";

    public static IReadOnlyList<string> All { get; } = [Scan, Sync, Overview, Schedule, Cleanup, Chat, Logs, Performance, About, Settings];

    public static IReadOnlyList<string> Navigable { get; } = [.. All, Docker];

    public static bool IsNavigable(string key)
    {
        return Navigable.Contains(key, StringComparer.OrdinalIgnoreCase);
    }

    public static string? Match(string key)
    {
        return Navigable.FirstOrDefault(known => string.Equals(known, key, StringComparison.OrdinalIgnoreCase));
    }
}
