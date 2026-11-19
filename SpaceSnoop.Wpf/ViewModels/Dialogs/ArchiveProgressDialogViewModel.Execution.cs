using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class ArchiveProgressDialogViewModel
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var stage = new Progress<ArchiveStageUpdate>(OnStage);

        _packProgress.Reset();
        _verifyProgress.Reset();
        _coverageProgress.Reset();

        await using (_progressPoller.StartScope())
        {
            await Task.Run(() => ExecuteZip(stage, token), token);
        }
    }

    protected override void OnStarting()
    {
        _logger.ArchiveStarted(_request.SourcePath);
    }

    protected override void OnFinished()
    {
        if (Cancelled)
        {
            _logger.ArchiveCancelled(_request.SourcePath);
            return;
        }

        if (Failure is { } failure)
        {
            _logger.ArchiveFailed(failure, _request.SourcePath);
            return;
        }

        _logger.ArchiveFinished(_request.SourcePath, _resultSummary);
    }

    protected override string BuildSummary()
    {
        if (Cancelled && !OriginalDeleted)
        {
            if (_deletingOriginal)
            {
                return $"Удаление оригинала отменено. Архив {TargetName} сохранён, часть исходных файлов могла быть удалена.";
            }

            return _packed
                ? $"Отменено на проверке: архив {TargetName} создан, но не проверен. Оригинал не тронут."
                : "Отменено.";
        }

        if (Failure is { } failure)
        {
            return $"Ошибка: {failure.Message}";
        }

        if (_coverageIssue is { } issue)
        {
            return $"Архив {_resultSummary} создан, но каталог покрыт не полностью: {issue}. Оригинал оставлен на месте.";
        }

        if (_unreadableIssue is { } skipped)
        {
            return $"Архив {_resultSummary}. Оригинал на месте: {skipped} – этих данных в архиве нет.";
        }

        if (!OriginalDeleted)
        {
            return $"Готово. Архив {_resultSummary}.";
        }

        return NeedsPermanentOriginalConfirm
            ? $"Готово. Архив {_resultSummary}. Оригинал удалён безвозвратно."
            : $"Готово. Архив {_resultSummary}. Оригинал → в корзину.";
    }

    private string DescribeFate()
    {
        if (!_request.DeleteOriginal)
        {
            return "Оригинал останется на месте.";
        }

        if (!NeedsPermanentOriginalConfirm)
        {
            return "Архив будет прочитан целиком с проверкой контрольных сумм, и только потом оригинал отправится в корзину.";
        }

        return ConfirmPermanentOriginalDelete
            ? "Архив будет прочитан целиком с проверкой контрольных сумм, и только потом оригинал будет удалён безвозвратно."
            : "Оригинал останется на месте. Корзина Windows на сетевом хранилище недоступна.";
    }

    private static string Summarize(long compressed, long original)
    {
        var ratio = original > 0 ? (1 - ((double)compressed / original)) * 100 : 0;

        return $"{SizeFormatter.Format(compressed)} (было {SizeFormatter.Format(original)}, −{ratio:F0} %)";
    }

    private void ExecuteZip(IProgress<ArchiveStageUpdate> stage, CancellationToken token)
    {
        var deleteOriginal = DeleteOriginalEffective;
        var permanent = deleteOriginal && NeedsPermanentOriginalConfirm;
        var content = _service.Collect(_request.SourcePath, token);
        _phase = ArchivePhase.Packing;
        stage.Report(new(ArchiveStage.Packing, content.Files.Count, content.Files.Count + content.EmptyDirectories.Count, content.Bytes));

        if (content.Unreadable.Count > 0)
        {
            _unreadableIssue = content.Unreadable.Count > 1
                ? $"не прочитано каталогов: {content.Unreadable.Count}, первый «{content.Unreadable[0]}»"
                : $"не прочитан каталог «{content.Unreadable[0]}»";
        }

        var stats = _service.ZipFiles(_request.SourcePath, content, _request.TargetPath, _request.Level, _packProgress, token);

        _packed = true;
        _verifyTotal = stats.Count;

        if (deleteOriginal)
        {
            _phase = ArchivePhase.Verifying;
        }

        if (permanent && _unreadableIssue is null)
        {
            var verified = false;
            var coverage = _service.VerifyCoverageAndDeleteSource(
                _request.SourcePath, _request.TargetPath, stats, _verifyProgress, _coverageProgress,
                () =>
                {
                    verified = true;
                    SetArchiveResult(stats);
                    BeginCoverage();
                },
                ReportTotals, BeginDeleting, token);

            if (!verified)
            {
                RejectArchive(coverage.Detail);
            }

            if (ApplyCoverage(coverage))
            {
                FinishDeleting();
            }

            return;
        }

        var (ok, detail) = _service.VerifyZip(_request.TargetPath, stats, deleteOriginal, _verifyProgress, token);
        if (!ok)
        {
            RejectArchive(detail);
        }

        token.ThrowIfCancellationRequested();
        SetArchiveResult(stats);
        if (!deleteOriginal || _unreadableIssue is not null)
        {
            return;
        }

        BeginCoverage();
        if (!ApplyCoverage(_service.VerifyCoverage(_request.SourcePath, _request.TargetPath, _coverageProgress, ReportTotals, token)))
        {
            return;
        }

        token.ThrowIfCancellationRequested();
        BeginDeleting();
        _deleteDirectory(_request.SourcePath, _request.Interactive);
        FinishDeleting();

        void RejectArchive(string reason)
        {
            _logger.ArchiveVerifyFailed(_request.TargetPath, reason);
            _service.SafeDelete(_request.TargetPath);
            throw new InvalidOperationException($"Архив не прошёл проверку ({reason}); оригинал не тронут.");
        }

        void BeginCoverage()
        {
            _coverageProgress.Reset();
            _coverageTotals = CoverageTotals.Empty;
            _phase = ArchivePhase.Coverage;
            stage.Report(new(ArchiveStage.Coverage, 0, 0, 0));
        }

        void ReportTotals(int files, long bytes)
        {
            _coverageTotals = new(files, bytes);
            stage.Report(new(ArchiveStage.Coverage, files, files, bytes));
        }

        bool ApplyCoverage(VerifyResult coverage)
        {
            if (!coverage.Ok)
            {
                _coverageIssue = coverage.Detail;
                _logger.ArchiveVerifyFailed(_request.TargetPath, coverage.Detail);
            }

            return coverage.Ok;
        }

        void BeginDeleting()
        {
            _deletingOriginal = true;
            _phase = ArchivePhase.None;
            stage.Report(new(ArchiveStage.Deleting, 0, 0, 0));
        }

        void FinishDeleting()
        {
            OriginalDeleted = true;
            var totals = _coverageTotals;
            stage.Report(new(ArchiveStage.Finished, totals.Files, Math.Max(_verifyTotal, 1), totals.Bytes));
        }
    }

    private void SetArchiveResult(ZipStats stats)
    {
        _resultSummary = Summarize(new FileInfo(_request.TargetPath).Length, stats.Bytes);
        CreatedArchivePath = _request.TargetPath;
    }
}
