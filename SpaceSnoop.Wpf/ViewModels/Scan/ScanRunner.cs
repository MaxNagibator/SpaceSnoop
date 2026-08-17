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

        return new(root, ScanNotes.ForTraversal(parallelism));
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

            if (!scan.IsComplete)
            {
                logger.MftScanIncomplete(directory.FullName,
                    scan.DroppedObjects,
                    scan.DroppedBytes,
                    scan.PartialRecords,
                    scan.UnknownSizeFiles,
                    scan.Report);
            }

            return new(scan.Root,
                new(true,
                    scan.Threads,
                    scan.ExtraNameBytes,
                    scan.DroppedObjects,
                    scan.DroppedBytes,
                    scan.UnknownSizeFiles,
                    scan.PartialRecords));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.MftFailed(exception.Unwrap(), directory.FullName);
            progress.Reset();
            return null;
        }
    }
}

public readonly record struct ScanOutcome(DirectorySpace Root, ScanNotes Notes);

public readonly record struct ScanNotes(
    bool UsedMft,
    int Parallelism,
    long ExtraNameBytes,
    long DroppedObjects,
    long DroppedBytes,
    long UnknownSizeFiles,
    long PartialRecords)
{
    public static ScanNotes ForTraversal(int parallelism)
    {
        return new(false, Math.Max(1, parallelism), 0, 0, 0, 0, 0);
    }

    public bool HasDrops => DroppedObjects > 0 || UnknownSizeFiles > 0 || PartialRecords > 0;

    public string Engine => UsedMft ? "mft" : "directories";
}
