namespace SpaceSnoop.Wpf.Bootstrap;

public readonly record struct ScheduleRequest(bool Enabled, string TaskName, ScheduleInterval Interval, TimeSpan Time, string Argument);

public readonly record struct ScheduleOutcome(bool Ok, string Error, ScheduleStatus Status);

public interface IScheduleRunner
{
    ScheduleOutcome Apply(ScheduleRequest request);

    void Remove(string taskName);
}

public sealed class ScheduleRunner : IScheduleRunner
{
    public ScheduleOutcome Apply(ScheduleRequest request)
    {
        if (request.Enabled)
        {
            if (!SyncScheduler.Create(request.TaskName, request.Interval, request.Time, request.Argument, out var error))
            {
                return new(false, error, ScheduleStatus.Missing);
            }
        }
        else if (SyncScheduler.Exists(request.TaskName))
        {
            SyncScheduler.Disable(request.TaskName, out _);
        }

        return new(true, string.Empty, SyncScheduler.Query(request.TaskName));
    }

    public void Remove(string taskName)
    {
        SyncScheduler.Remove(taskName, out _);
    }
}
