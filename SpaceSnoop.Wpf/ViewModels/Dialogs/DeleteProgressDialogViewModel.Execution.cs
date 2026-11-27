using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class DeleteProgressDialogViewModel
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var progress = new DeleteProgressState(Items.Count);
        _progress = progress;
        var deletedItems = new List<SpaceBase>(Items.Count);
        DeletedItems = deletedItems;

        try
        {
            await Task.Run(() => RunDeletion(deletedItems, progress, token), token);
        }
        finally
        {
            await _progressPoller.StopAsync();
            _progress = null;
            ClearPulse();
        }
    }

    protected override void OnStarting()
    {
        ExcludeCommand.NotifyCanExecuteChanged();
        SelectPreviousCommand.NotifyCanExecuteChanged();
        SelectNextCommand.NotifyCanExecuteChanged();
        _diagnostics = new(_allPermanent, Items.Count, _totalBytes, _monitor, _operations, _preferences);
        _diagnostics.Start();
        _progressPoller.Start();
        _logger.DeletionStarted(_runId, Items.Count, SizeFormatter.Format(_totalBytes), _permanentCount > 0);
    }

    protected override void OnFinished()
    {
        _progressPoller.Stop();

        _diagnostics.Finish(_processed, _freedBytes, !Cancelled && Failure is null);
        DiagnosticsText = null;

        if (Failure is { } failure)
        {
            _logger.DeletionFailed(failure, _runId, _processed, Items.Count);
        }

        _logger.DeletionFinished(_runId, Cancelled, _deleted, _failed, SizeFormatter.Format(_freedBytes));
    }

    protected override string BuildSummary()
    {
        if (Failure is { } failure)
        {
            return $"Ошибка: {failure.Message}";
        }

        var head = Cancelled ? "Отменено." : "Готово.";
        if (!Cancelled && _failed > 0)
        {
            head = "Завершено с ошибками.";
        }

        var errors = _failed > 0 ? $" Ошибок: {_failed}." : string.Empty;
        var partial = Items.Any(static row => row.IsPermanent && row.IsDirectory && row.State == DeleteRowState.Failed)
            ? " Каталоги с ошибкой могли быть удалены частично. Повторите сканирование оставшихся данных."
            : string.Empty;

        if (IsMixed)
        {
            var recycledDone = Items.Count(static row => !row.IsPermanent && row.State == DeleteRowState.Done);
            var permanentDone = Items.Count(static row => row.IsPermanent && row.State == DeleteRowState.Done);
            return $"{head} В корзину: {recycledDone} из {_recycleCount}, безвозвратно: {permanentDone} из {_permanentCount}. Освобождено: {SizeFormatter.Format(_freedBytes)}.{errors}{partial}";
        }

        var verb = _allPermanent ? "Удалено безвозвратно" : "Перемещено в корзину";
        return $"{head} {verb}: {_deleted} из {Items.Count}. Освобождено: {SizeFormatter.Format(_freedBytes)}.{errors}{partial}";
    }

    private static string DescribeRange(in DeleteChunkInfo chunk)
    {
        return chunk.FirstIndex == chunk.LastIndex
            ? $"{chunk.FirstIndex + 1}"
            : $"{chunk.FirstIndex + 1}–{chunk.LastIndex + 1}";
    }

    private List<DeleteBatchSegment> BuildSegments(
        in DeleteBatchCallbacks recycleCallbacks,
        in DeleteBatchCallbacks permanentCallbacks)
    {
        var segments = new List<DeleteBatchSegment>();
        var index = 0;

        while (index < Items.Count)
        {
            var start = index;
            var permanent = Items[index].IsPermanent;
            index++;

            while (index < Items.Count && Items[index].IsPermanent == permanent)
            {
                index++;
            }

            segments.Add(new(
                start,
                index - start,
                permanent ? 1 : AppDefaults.DeleteRecycleChunkSize,
                permanent ? permanentCallbacks : recycleCallbacks));
        }

        return segments;
    }

    private void RunDeletion(List<SpaceBase> deletedItems, DeleteProgressState progress, CancellationToken token)
    {
        var logPath = Path.Combine(AppStorage.DataDirectory, AppInfo.DeletionLogFileName);
        long freed = 0;
        var completed = 0;
        var failed = 0;
        var journalBroken = false;
        var isMixed = IsMixed;
        var paths = new string[Items.Count];

        for (var i = 0; i < paths.Length; i++)
        {
            paths[i] = Items[i].Path;
        }

        var recycleCallbacks = _diagnostics.BuildCallbacks(false);
        var permanentCallbacks = _diagnostics.BuildCallbacks(true);
        var segments = BuildSegments(recycleCallbacks, permanentCallbacks);
        var writer = new StreamWriter(logPath, true);

        try
        {
            var modeLabel = isMixed
                ? $"в корзину: {_recycleCount}, безвозвратно: {_permanentCount}"
                : _allPermanent
                    ? "безвозвратно"
                    : "в корзину";
            Journal($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Удаление {Items.Count} элемент(ов) ({modeLabel})");

            DeleteBatch.RunSegments(
                paths,
                segments,
                new(OnStarting, OnResult, OnChunkStarting, OnChunkFinished),
                token);
        }
        finally
        {
            try
            {
                writer.Dispose();
            }
            catch (Exception exception)
            {
                ReportJournalFailure(exception);
            }
        }

        void Journal(string line)
        {
            if (journalBroken)
            {
                return;
            }

            try
            {
                writer.WriteLine(line);
            }
            catch (Exception exception)
            {
                ReportJournalFailure(exception);
            }
        }

        void ReportJournalFailure(Exception exception)
        {
            if (journalBroken)
            {
                return;
            }

            journalBroken = true;

            try
            {
                _logger.DeletionLogWriteFailed(exception, logPath);
            }
            catch (Exception)
            {
            }
        }

        void OnStarting(int index)
        {
            progress.Report(new(index, DeleteRowState.Deleting, null, freed, completed, failed));
        }

        void OnChunkStarting(DeleteChunkInfo chunk)
        {
            progress.ReportChunkStarted(chunk);
            _logger.DeleteChunkStarted(_runId, chunk.Ordinal, chunk.Total, DescribeRange(chunk));
        }

        void OnChunkFinished(DeleteChunkOutcome outcome)
        {
            progress.ReportChunkFinished();

            var chunk = outcome.Chunk;
            var range = DescribeRange(chunk);

            _logger.DeleteChunkFinished(_runId, chunk.Ordinal, chunk.Total, range, outcome.ElapsedMs, outcome.RetriedOneByOne);

            if (outcome.ElapsedMs >= AppDefaults.DeleteChunkWarnMs)
            {
                _logger.DeleteChunkSlow(_runId, chunk.Ordinal, chunk.Total, range, outcome.ElapsedMs, AppDefaults.DeleteChunkWarnMs);
            }
        }

        void OnResult(DeleteItemResult result)
        {
            var path = paths[result.Index];

            switch (result.Status)
            {
                case DeleteItemStatus.Removed:
                    Journal(isMixed ? $"{(Items[result.Index].IsPermanent ? "БЕЗВОЗВРАТНО" : "В КОРЗИНУ")} {path}" : path);
                    completed++;
                    freed += Items[result.Index].Space.TotalSize;
                    deletedItems.Add(Items[result.Index].Space);
                    progress.Report(new(result.Index, DeleteRowState.Done, null, freed, completed, failed));
                    break;

                case DeleteItemStatus.Missing:
                    Journal($"ПРОПУЩЕНО (не найден) {path}");
                    _logger.DeleteItemMissing(path);
                    failed++;
                    progress.Report(new(result.Index, DeleteRowState.Failed, "Путь не найден", freed, completed, failed));
                    break;

                case DeleteItemStatus.Failed when result.Failure is { } failure:
                    Journal($"ОШИБКА {path}: {failure.Message}");
                    _logger.DeleteItemFailed(failure, path);
                    failed++;
                    progress.Report(new(result.Index, DeleteRowState.Failed, failure.Message, freed, completed, failed));
                    break;

                default:
                    progress.Report(new(result.Index, DeleteRowState.Pending, null, freed, completed, failed));
                    break;
            }
        }
    }
}
