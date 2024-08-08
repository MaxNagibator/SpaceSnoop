using System.ComponentModel;
using System.Diagnostics;
using SpaceSnoop.Extensions;

namespace SpaceSnoop;

public partial class MainForm
{
    private readonly BackgroundWorker _backgroundWorker;
    private CancellationTokenSource? _cancellationTokenSource;

    private void InitializeWorker()
    {
        _backgroundWorker.DoWork += OnDoWork;
        _backgroundWorker.RunWorkerCompleted += OnRunWorkerCompleted;

        _backgroundWorker.WorkerSupportsCancellation = true;
    }

    private void FinalizeWorker()
    {
        _backgroundWorker.DoWork -= OnDoWork;
        _backgroundWorker.RunWorkerCompleted -= OnRunWorkerCompleted;
    }

    private void OnDoWork(object? sender, DoWorkEventArgs args)
    {
        if (args.Argument is not WorkerRequest(var disk, var cancellationToken)
            || string.IsNullOrWhiteSpace(disk))
        {
            return;
        }

        DirectoryInfo directory = new(disk);

        if (directory.Exists == false)
        {
            _logger.LogError("Расчет для каталога {Directory} невозможен. Директория не найдена.", directory.FullName);
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        DirectoryInfo directoryInfo = new(disk);

        try
        {
            DirectorySpace directorySpace = _useMultithreadingCheckBox.Checked
                ? _diskSpaceCalculator.CalculateMultithreaded(directoryInfo, cancellationToken)
                : _diskSpaceCalculator.Calculate(directoryInfo, cancellationToken);

            args.Result = directorySpace;
        }
        catch (OperationCanceledException)
        {
            args.Cancel = true;
        }
        finally
        {
            if (args.Cancel == false)
            {
                _logger.LogInformation("Расчет для каталога {Directory} завершен за {ElapsedSeconds:F2} с ({ElapsedMilliseconds} мс).",
                    directory.FullName, stopwatch.Elapsed.TotalSeconds, stopwatch.ElapsedMilliseconds);
            }

            stopwatch.Stop();
        }
    }

    private void OnRunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs args)
    {
        StopProgressBar();

        if (args.Cancelled)
        {
            _logger.LogInformation("Сканирование было отменено пользователем.");
        }
        else if (args.Error != null)
        {
            _logger.LogError(args.Error, "Произошла ошибка во время сканирования.");
        }
        else if (args.Result is DirectorySpace data)
        {
            TreeNode addedParent = _directoriesTreeView.Nodes.AddDirectoryNode(data).AddDirectoryNodes(data);
            UpdateNodeColors(addedParent);
            SortNodes();
        }
    }

    private void StopWorker()
    {
        _cancellationTokenSource?.Cancel();
    }

    private record WorkerRequest(string Disk, CancellationToken CancellationToken);
}
