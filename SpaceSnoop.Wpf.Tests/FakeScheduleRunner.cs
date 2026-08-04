using SpaceSnoop.Wpf.Bootstrap;

namespace SpaceSnoop.Wpf.Tests;

internal sealed class FakeScheduleRunner : IScheduleRunner
{
    public List<ScheduleRequest> Applied { get; } = [];

    public List<string> Removed { get; } = [];

    public Action<int>? OnCall { get; set; }

    public Func<ScheduleRequest, bool>? Fails { get; set; }

    public ScheduleOutcome Apply(ScheduleRequest request)
    {
        Applied.Add(request);
        OnCall?.Invoke(Applied.Count + Removed.Count);

        return Fails?.Invoke(request) == true
            ? new(false, "планировщик отказал", ScheduleStatus.Missing)
            : new(true, string.Empty, ScheduleStatus.Missing);
    }

    public void Remove(string taskName)
    {
        Removed.Add(taskName);
        OnCall?.Invoke(Applied.Count + Removed.Count);
    }
}
