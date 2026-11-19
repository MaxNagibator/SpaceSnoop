using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel
{
    private Task<ScanOutcome> RunTraversalAsync(
        DirectoryInfo directory,
        int parallelism,
        ScanProgress progress,
        CancellationToken token)
    {
        return Task.Run(() => _runner.Run(directory, parallelism, progress, token), token);
    }
}
