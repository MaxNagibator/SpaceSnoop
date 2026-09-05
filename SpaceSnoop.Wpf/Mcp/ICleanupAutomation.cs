namespace SpaceSnoop.Wpf.Mcp;

public enum CleanupConsent
{
    None = 0,
    Granted = 1,
    Declined = 2,
    TimedOut = 3,
    Nothing = 4,
    Busy = 5,
    Stale = 6,
}

public readonly record struct CleanupOutcome(
    CleanupConsent Consent,
    long FreedBytes,
    int Deleted,
    int Skipped,
    bool Cancelled,
    bool Failed,
    string StatusText);

public interface ICleanupAutomation
{
    bool IsBusy { get; }

    bool IsModalBusy { get; }

    Task<CleanupOutcome> CleanFromAutomationAsync(IReadOnlyList<string> targetIds, CancellationToken cancellationToken);
}
