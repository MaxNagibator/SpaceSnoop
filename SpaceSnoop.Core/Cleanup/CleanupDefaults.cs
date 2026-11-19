namespace SpaceSnoop.Core.Cleanup;

internal static class CleanupDefaults
{
    internal const int MaxReportedErrors = 50;
    internal static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(15);
}
