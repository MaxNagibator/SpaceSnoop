using KeepShell.ViewModels;
using Microsoft.VisualBasic.FileIO;
using System.Collections.ObjectModel;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class DeleteProgressDialogViewModel : OperationDialogViewModelBase
{
    private readonly bool _permanent;
    private readonly long _totalBytes;
    private readonly ILogger _logger;

    private long _freedBytes;
    private int _deleted;
    private int _failed;

    [ObservableProperty]
    private string _freedText;

    public DeleteProgressDialogViewModel(IReadOnlyList<SpaceBase> items, bool permanent, ILogger logger)
    {
        _permanent = permanent;
        _logger = logger;
        Items = new(items.Select(static item => new DeleteRowViewModel(item)));
        _totalBytes = items.Sum(static item => item.TotalSize);

        ActionVerb = permanent ? "удалены безвозвратно" : "перемещены в корзину";
        IntroText = $"Будут {ActionVerb}: {Plural.Format(Items.Count, "объект", "объекта", "объектов")} · {SizeFormatter.Format(_totalBytes)}";
        CountText = $"0 / {Items.Count}";
        _freedText = SizeFormatter.Format(0);
    }

    public ObservableCollection<DeleteRowViewModel> Items { get; }

    public IReadOnlyList<SpaceBase> DeletedItems { get; private set; } = [];

    public override string Title => "Удаление";

    public string ActionVerb { get; }

    public string IntroText { get; }

    protected override string RunningStatus => "Удаление…";

    protected override bool CloseResult => _deleted > 0;

    protected override bool HasFailedItems => _failed > 0;

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var progress = new Progress<DeleteTick>(OnTick);

        DeletedItems = await Task.Run(() => RunDeletion(progress, token), token);
    }

    protected override void OnStarting()
    {
        _logger.DeletionStarted(Items.Count, SizeFormatter.Format(_totalBytes), _permanent);
    }

    protected override void OnFinished()
    {
        if (Failure is { } failure)
        {
            _logger.DeleteItemFailed(failure, CurrentPath);
        }

        _logger.DeletionFinished(Cancelled, _deleted, _failed, SizeFormatter.Format(_freedBytes));
    }

    protected override bool CanStart()
    {
        return base.CanStart() && Items.Count > 0;
    }

    protected override string BuildSummary()
    {
        if (Failure is { } failure)
        {
            return $"Ошибка: {failure.Message}";
        }

        var verb = _permanent ? "Удалено безвозвратно" : "Перемещено в корзину";
        var head = Cancelled ? "Отменено." : "Готово.";
        var errors = _failed > 0 ? $" Ошибок: {_failed}." : string.Empty;
        return $"{head} {verb}: {_deleted} из {Items.Count}. Освобождено: {SizeFormatter.Format(_freedBytes)}.{errors}";
    }

    private void OnTick(DeleteTick tick)
    {
        var row = Items[tick.Index];
        row.State = tick.State;

        if (tick.Error is not null)
        {
            row.Error = tick.Error;
        }

        CurrentPath = row.Path;
        _freedBytes = tick.Freed;
        _deleted = tick.Completed;
        _failed = tick.Failed;
        CountText = $"{tick.Completed} / {Items.Count}";
        FreedText = SizeFormatter.Format(tick.Freed);
        ProgressValue = _totalBytes > 0 ? Math.Clamp((double)tick.Freed / _totalBytes, 0d, 1d) : 0d;
    }

    private List<SpaceBase> RunDeletion(IProgress<DeleteTick> progress, CancellationToken token)
    {
        var logPath = Path.Combine(AppStorage.DataDirectory, AppInfo.DeletionLogFileName);
        long freed = 0;
        var completed = 0;
        var failed = 0;
        var deletedItems = new List<SpaceBase>(Items.Count);

        using var writer = new StreamWriter(logPath, true);
        var buffer = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Удаление {Items.Count} элемент(ов) ({(_permanent ? "безвозвратно" : "в корзину")})";
        writer.WriteLine(buffer);

        for (var i = 0; i < Items.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            var space = Items[i].Space;
            progress.Report(new(i, DeleteRowState.Deleting, null, freed, completed, failed));

            var result = DeleteItem(space, writer);

            if (result.Removed)
            {
                completed++;
                freed += space.TotalSize;
                deletedItems.Add(space);
                progress.Report(new(i, DeleteRowState.Done, null, freed, completed, failed));
            }
            else
            {
                failed++;
                progress.Report(new(i, DeleteRowState.Failed, result.Error, freed, completed, failed));
            }
        }

        return deletedItems;
    }

    private DeleteResult DeleteItem(SpaceBase space, TextWriter writer)
    {
        var path = space.AbsolutePath;

        try
        {
            if (!DeletePath(path))
            {
                writer.WriteLine($"ПРОПУЩЕНО (не найден) {path}");
                _logger.DeleteItemMissing(path);
                return new(false, "Путь не найден");
            }

            writer.WriteLine(path);
            return new(true, null);
        }
        catch (Exception exception)
        {
            writer.WriteLine($"ОШИБКА {path}: {exception.Message}");
            _logger.DeleteItemFailed(exception, path);
            return new(false, exception.Message);
        }
    }

    private bool DeletePath(string path)
    {
        if (Directory.Exists(path))
        {
            DeleteDirectoryPath(path);
            return true;
        }

        if (File.Exists(path))
        {
            DeleteFilePath(path);
            return true;
        }

        return false;
    }

    private void DeleteDirectoryPath(string path)
    {
        if (_permanent)
        {
            Directory.Delete(path, true);
        }
        else
        {
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
    }

    private void DeleteFilePath(string path)
    {
        if (_permanent)
        {
            File.Delete(path);
        }
        else
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
    }

    private readonly record struct DeleteTick(
        int Index,
        DeleteRowState State,
        string? Error,
        long Freed,
        int Completed,
        int Failed);

    private readonly record struct DeleteResult(bool Removed, string? Error);
}
