using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed class ScanRunner(
    DiskSpaceCalculator calculator,
    MftScanner mftScanner,
    ScanPreferences preferences,
    ILogger<ScanRunner> logger)
{
    public ScanOutcome Run(DirectoryInfo directory, int parallelism, ScanProgress progress, CancellationToken cancel)
    {
        if (preferences.MftEnabled && TryReadMft(directory, progress, cancel) is { } outcome)
        {
            return outcome;
        }

        var root = parallelism > 1
            ? calculator.CalculateMultithreaded(directory, parallelism, progress, cancel)
            : calculator.Calculate(directory, progress, cancel);

        return new(root, 0, false);
    }

    private ScanOutcome? TryReadMft(DirectoryInfo directory, ScanProgress progress, CancellationToken cancel)
    {
        var availability = MftScanner.Probe(directory.FullName);

        if (availability != MftAvailability.Available)
        {
            logger.MftUnavailable(directory.FullName, availability);
            return null;
        }

        try
        {
            var scan = mftScanner.Calculate(directory, progress, cancel);
            return new(scan.Root, scan.ExtraNameBytes, true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.MftFailed(exception.Unwrap(), directory.FullName);
            progress.Reset();
            return null;
        }
    }
}

public readonly record struct ScanOutcome(DirectorySpace Root, long ExtraNameBytes, bool UsedMft);
