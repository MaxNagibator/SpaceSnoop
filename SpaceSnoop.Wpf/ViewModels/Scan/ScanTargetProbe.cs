using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

internal static class ScanTargetProbe
{
    private static readonly Func<string, bool> DefaultDirectoryExists = Directory.Exists;

    public static Func<string, bool> DirectoryExists { get; set; } = DefaultDirectoryExists;

    public static Func<DirectoryInfo, string?> RootRefusal { get; set; } = DiskSpaceCalculator.DescribeRootRefusal;

    public static Task<PathProbeResult> CheckAsync(string path, CancellationToken cancellationToken)
    {
        return CheckAsync(path, AppDefaults.PathProbeTimeoutMs, cancellationToken);
    }

    internal static Task<PathProbeResult> CheckAsync(string path, int timeoutMs, CancellationToken cancellationToken)
    {
        return PathProbe.CheckAsync(path, DirectoryExists, timeoutMs, cancellationToken);
    }

    internal static Task<PathRefusalProbeResult> CheckForScanAsync(
        string path,
        DirectoryInfo directory,
        CancellationToken cancellationToken)
    {
        return CheckForScanAsync(path, directory, AppDefaults.PathProbeTimeoutMs, cancellationToken);
    }

    internal static Task<PathRefusalProbeResult> CheckForScanAsync(
        string path,
        DirectoryInfo directory,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var exists = DirectoryExists;
        var refusal = RootRefusal;
        return PathProbe.CheckWithRefusalAsync(path, exists, () => refusal(directory), timeoutMs, cancellationToken);
    }

    public static async Task<string> DescribeMissingAsync(string path, CancellationToken cancellationToken)
    {
        if (UncPath.IsUnc(path) && ReferenceEquals(DirectoryExists, DefaultDirectoryExists))
        {
            try
            {
                await Task.Run(
                        () =>
                        {
                            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
                            _ = entries.MoveNext();
                        },
                        cancellationToken)
                    .WaitAsync(TimeSpan.FromMilliseconds(AppDefaults.PathProbeTimeoutMs), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException)
            {
                return $"Сетевой каталог «{path}» не отвечает (превышено время ожидания).";
            }
            catch (Exception exception)
            {
                return NetworkErrorText.Describe(exception, path, AdminElevation.IsElevated);
            }
        }

        return $"Каталог не найден: {path}";
    }

    public static string FormatTimeoutMessage(string path)
    {
        return UncPath.IsUnc(path)
            ? $"Сетевой каталог «{path}» не отвечает (превышено время ожидания)."
            : $"Каталог «{path}» не отвечает (превышено время ожидания).";
    }

    public static string DescribeScanError(Exception exception, string path)
    {
        return UncPath.IsUnc(path)
            ? NetworkErrorText.Describe(exception, path, AdminElevation.IsElevated)
            : exception.Message;
    }
}
