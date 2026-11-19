using Microsoft.Extensions.Logging.Abstractions;
using System.ComponentModel;
using System.Diagnostics;

namespace SpaceSnoop.Services;

public sealed class SyncWorkerService : IDisposable
{
    private readonly BackgroundWorker _backgroundWorker;

    public SyncWorkerService()
    {
        _backgroundWorker = new();
        _backgroundWorker.DoWork += OnDoWork;
        _backgroundWorker.RunWorkerCompleted += OnRunWorkerCompleted;
        _backgroundWorker.WorkerSupportsCancellation = true;
    }

    public event EventHandler<CompareResponse?>? CompareCompleted;
    public event EventHandler<HashResponse?>? HashCompleted;
    public event EventHandler<SyncResponse?>? SyncCompleted;

    public bool IsBusy => _backgroundWorker.IsBusy;

    public void StartCompare(CompareDirectoriesRequest compareRequest, CancellationToken cancellationToken)
    {
        if (_backgroundWorker.IsBusy)
        {
            return;
        }

        var request = new CompareRequest(compareRequest, cancellationToken);
        _backgroundWorker.RunWorkerAsync(request);
    }

    public void StartSync(ComparisonResult comparisonResult, CancellationToken cancellationToken)
    {
        if (_backgroundWorker.IsBusy)
        {
            return;
        }

        var request = new SyncRequest(comparisonResult, cancellationToken);
        _backgroundWorker.RunWorkerAsync(request);
    }

    public void StartHash(ComparisonResult comparisonResult, CancellationToken cancellationToken)
    {
        if (_backgroundWorker.IsBusy)
        {
            return;
        }

        var request = new HashRequest(comparisonResult, cancellationToken);
        _backgroundWorker.RunWorkerAsync(request);
    }

    public void Dispose()
    {
        _backgroundWorker.DoWork -= OnDoWork;
        _backgroundWorker.RunWorkerCompleted -= OnRunWorkerCompleted;
        _backgroundWorker.Dispose();
        GC.SuppressFinalize(this);
    }

    private static void OnDoWork(object? sender, DoWorkEventArgs args)
    {
        Stopwatch stopwatch;

        switch (args.Argument)
        {
            case CompareRequest(var compareRequest, var token):
                stopwatch = Stopwatch.StartNew();

                try
                {
                    var compare = new CompareDirectoriesUseCase(NullLogger<DirectoryComparer>.Instance);
                    var result = compare.Execute(compareRequest, token);
                    stopwatch.Stop();
                    args.Result = new CompareResponse(result, stopwatch.Elapsed, null);
                }
                catch (OperationCanceledException)
                {
                    stopwatch.Stop();
                    args.Result = new CompareResponse(null, stopwatch.Elapsed, null);
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    args.Result = new CompareResponse(null, stopwatch.Elapsed, ex.Message);
                }

                break;

            case HashRequest(var hashResult, var token):
                stopwatch = Stopwatch.StartNew();

                try
                {
                    HashModifiedFiles(hashResult.Root, hashResult.LeftPath, hashResult.RightPath, token);
                    stopwatch.Stop();
                    args.Result = new HashResponse(hashResult, stopwatch.Elapsed, null);
                }
                catch (OperationCanceledException)
                {
                    stopwatch.Stop();
                    args.Result = new HashResponse(null, stopwatch.Elapsed, null);
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    args.Result = new HashResponse(null, stopwatch.Elapsed, ex.Message);
                }

                break;

            case SyncRequest(var comparisonResult, var token):
                stopwatch = Stopwatch.StartNew();

                try
                {
                    var sync = new ExecuteSyncUseCase(NullLogger<SyncEngine>.Instance);
                    var report = sync.Execute(new(comparisonResult), token);
                    stopwatch.Stop();
                    args.Result = new SyncResponse(report, stopwatch.Elapsed, null);
                }
                catch (OperationCanceledException)
                {
                    stopwatch.Stop();
                    args.Result = new SyncResponse(null, stopwatch.Elapsed, null);
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    args.Result = new SyncResponse(null, stopwatch.Elapsed, ex.Message);
                }

                break;
        }
    }

    private void OnRunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs args)
    {
        string? error = null;

        if (args.Cancelled)
        {
            error = "Операция была отменена";
        }
        else if (args.Error != null)
        {
            error = args.Error.Message;
        }

        switch (args.Result)
        {
            case CompareResponse response:
                CompareCompleted?.Invoke(this, response);
                break;

            case SyncResponse response:
                SyncCompleted?.Invoke(this, response);
                break;

            case HashResponse response:
                HashCompleted?.Invoke(this, response);
                break;

            default:
                if (error != null)
                {
                    CompareCompleted?.Invoke(this, new(null, TimeSpan.Zero, error));
                }

                break;
        }
    }

    private static void HashModifiedFiles(DirectoryComparison dir, string leftBase, string rightBase, CancellationToken cancellationToken)
    {
        foreach (var file in dir.Files.Where(f => f.Status == ComparisonStatus.Modified))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var leftPath = Path.Combine(leftBase, file.RelativePath);
            var rightPath = Path.Combine(rightBase, file.RelativePath);

            try
            {
                file.LeftHash = FileHasher.ComputeHash(leftPath, cancellationToken);
                file.RightHash = FileHasher.ComputeHash(rightPath, cancellationToken);

                if (file.LeftHash == file.RightHash)
                {
                    file.Status = ComparisonStatus.Identical;
                    file.Action = SyncAction.Skip;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Ошибка хеширования {file.RelativePath}: {exception.Message}");
            }
        }

        foreach (var sub in dir.SubDirectories)
        {
            HashModifiedFiles(sub, leftBase, rightBase, cancellationToken);
        }
    }

    public sealed record CompareResponse(ComparisonResult? Result, TimeSpan Elapsed, string? Error);

    public sealed record SyncResponse(SyncReport? Report, TimeSpan Elapsed, string? Error);

    public sealed record HashResponse(ComparisonResult? Result, TimeSpan Elapsed, string? Error);

    private sealed record CompareRequest(CompareDirectoriesRequest Request, CancellationToken CancellationToken);

    private sealed record SyncRequest(ComparisonResult ComparisonResult, CancellationToken CancellationToken);

    private sealed record HashRequest(ComparisonResult ComparisonResult, CancellationToken CancellationToken);
}
