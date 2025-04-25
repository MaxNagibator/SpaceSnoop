using System.ComponentModel;
using System.Diagnostics;

namespace SpaceSnoop.Services;

public class WorkerService : IDisposable
{
    private readonly BackgroundWorker _backgroundWorker;
    private readonly DiskSpaceCalculator _diskSpaceCalculator;
    private readonly ILogger<WorkerService> _logger;

    public WorkerService(DiskSpaceCalculator diskSpaceCalculator, BackgroundWorker backgroundWorker, ILogger<WorkerService> logger)
    {
        _diskSpaceCalculator = diskSpaceCalculator;
        _backgroundWorker = backgroundWorker;
        _logger = logger;

        Initialize();
    }

    public event EventHandler<DirectorySpace?>? WorkCompleted;

    public void Dispose()
    {
        _backgroundWorker.DoWork -= OnDoWork;
        _backgroundWorker.RunWorkerCompleted -= OnRunWorkerCompleted;

        _backgroundWorker.Dispose();

        GC.SuppressFinalize(this);
    }

    public void StartWorker(string disk, bool isMultithread, CancellationToken cancellationToken)
    {
        var workerRequest = new WorkerRequest(disk, isMultithread, cancellationToken);
        _backgroundWorker.RunWorkerAsync(workerRequest);
    }

    private void OnDoWork(object? sender, DoWorkEventArgs args)
    {
        if (args.Argument is not WorkerRequest(var disk, var isMultithread, var cancellationToken) || string.IsNullOrWhiteSpace(disk))
        {
            return;
        }

        var directory = new DirectoryInfo(disk);

        if (!directory.Exists)
        {
            _logger.LogError("Расчет для каталога {Directory} невозможен. Директория не найдена.", directory.FullName);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var directoryInfo = new DirectoryInfo(disk);

        try
        {
            var directorySpace = isMultithread
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
        DirectorySpace? data = null;

        if (args.Cancelled)
        {
            _logger.LogInformation("Сканирование было отменено пользователем.");
        }
        else if (args.Error != null)
        {
            _logger.LogError(args.Error, "Произошла ошибка во время сканирования.");
        }
        else if (args.Result is DirectorySpace space)
        {
            data = space;
        }

        WorkCompleted?.Invoke(this, data);
    }

    private void Initialize()
    {
        _backgroundWorker.DoWork += OnDoWork;
        _backgroundWorker.RunWorkerCompleted += OnRunWorkerCompleted;
        _backgroundWorker.WorkerSupportsCancellation = true;
    }

    private record WorkerRequest(string Disk, bool IsMultithread, CancellationToken CancellationToken);
}
