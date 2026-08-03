namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanSummaryViewModel : ObservableObject
{
    private const string NoValue = "–";

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _resultPath = string.Empty;

    [ObservableProperty]
    private string _resultSizeText = NoValue;

    [ObservableProperty]
    private string _resultFileCountText = NoValue;

    [ObservableProperty]
    private string _resultDirCountText = NoValue;

    [ObservableProperty]
    private string _resultElapsedText = NoValue;

    [ObservableProperty]
    private string _resultRateText = NoValue;

    public PerformanceOperation Apply(DirectorySpace result, TimeSpan elapsed)
    {
        ResultPath = result.AbsolutePath;
        Refresh(result);
        ResultElapsedText = PerformanceFormat.Elapsed(elapsed);

        var run = new PerformanceOperation("Сканирование", result.TotalFileCount, result.Size, elapsed);
        ResultRateText = PerformanceFormat.Rate(run) ?? NoValue;

        return run;
    }

    public void Refresh(DirectorySpace result)
    {
        ResultSizeText = result.TotalSizeText;
        ResultFileCountText = result.TotalFileCount.ToString("N0");
        ResultDirCountText = result.TotalDirectoryCount.ToString("N0");
    }
}
