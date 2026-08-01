namespace SpaceSnoop.Wpf.Bootstrap;

public static class SectionKey
{
    public const string Scan = "scan";
    public const string Sync = "sync";
    public const string Overview = "overview";
    public const string Schedule = "schedule";
    public const string Docker = "docker";
    public const string Chat = "chat";
    public const string Logs = "logs";
    public const string Performance = "performance";
    public const string About = "about";
    public const string Settings = "settings";

    public static IReadOnlyList<string> All { get; } = [Scan, Sync, Overview, Schedule, Docker, Chat, Logs, Performance, About, Settings];
}
