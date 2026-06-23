namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class SyncProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Left { get; set; } = string.Empty;
    public string Right { get; set; } = string.Empty;
    public int Mode { get; set; }
    public bool Mirror { get; set; }
    public string Exclusions { get; set; } = string.Empty;
    public ScheduleInterval Interval { get; set; } = ScheduleInterval.Daily;
    public string Time { get; set; } = "03:00";
    public bool Enabled { get; set; }
}
