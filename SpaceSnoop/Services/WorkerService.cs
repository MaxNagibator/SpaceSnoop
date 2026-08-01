using System.ComponentModel;
using System.Diagnostics;

namespace SpaceSnoop.Services;

public sealed class WorkerService : IDisposable
{
    private readonly BackgroundWorker _backgroundWorker;
    private readonly DiskSpaceCalculator _diskSpaceCalculator;

    public WorkerService(DiskSpaceCalculator diskSpaceCalculator, BackgroundWorker backgroundWorker)
    {
        _diskSpaceCalculator = diskSpaceCalculator;
        _backgroundWorker = backgroundWorker;

        Initialize();
    }

    public event EventHandler<Response?>? WorkCompleted;

    public void Dispose()
    {
        _backgroundWorker.DoWork -= OnDoWork;
        _backgroundWorker.RunWorkerCompleted -= OnRunWorkerCompleted;

        _backgroundWorker.Dispose();

        GC.SuppressFinalize(this);
    }

    public void StartWorker(string disk, bool isMultithread, CancellationToken cancellationToken)
    {
        var request = new Request(disk, isMultithread, cancellationToken);
        _backgroundWorker.RunWorkerAsync(request);
    }

    private void OnDoWork(object? sender, DoWorkEventArgs args)
    {
        if (args.Argument is not Request(var disk, var isMultithread, var cancellationToken) || string.IsNullOrWhiteSpace(disk))
        {
            return;
        }

        DirectorySpace? directorySpace = null;
        string? error = null;

        var stopwatch = Stopwatch.StartNew();
        var directory = new DirectoryInfo(disk);

        if (directory.Exists)
        {
            var directoryInfo = new DirectoryInfo(disk);

            try
            {
                directorySpace = isMultithread
                    ? _diskSpaceCalculator.CalculateMultithreaded(directoryInfo, cancellationToken)
                    : _diskSpaceCalculator.Calculate(directoryInfo, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                args.Cancel = true;
            }
            finally
            {
                stopwatch.Stop();
            }
        }
        else
        {
            error = "Директория не найдена";
        }

        args.Result = new Response(directorySpace, stopwatch.Elapsed, error);
    }

    private void OnRunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs args)
    {
        Response? data = null;
        string? error = null;

        if (args.Cancelled)
        {
            error = "Сканирование было отменено пользователем";
        }
        else if (args.Error != null)
        {
            error = "Произошла ошибка во время сканирования";
        }
        else if (args.Result is Response response)
        {
            data = response;
        }

        WorkCompleted?.Invoke(this, data ?? new Response(null, TimeSpan.Zero, error));
    }

    private void Initialize()
    {
        _backgroundWorker.DoWork += OnDoWork;
        _backgroundWorker.RunWorkerCompleted += OnRunWorkerCompleted;
        _backgroundWorker.WorkerSupportsCancellation = true;
    }

    public record Response(DirectorySpace? DirectorySpace, TimeSpan Elapsed, string? Error);

    private sealed record Request(string Disk, bool IsMultithread, CancellationToken CancellationToken);
}
