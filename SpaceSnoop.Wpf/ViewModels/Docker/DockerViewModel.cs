using KeepShell.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Docker;

public sealed partial class DockerViewModel : ObservableObject, IPageHeader, IPageRefresh, IPageStatus
{
    internal const string PollingStatus = "Опрашиваю Docker…";

    private readonly DockerService _docker;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger<DockerViewModel> _logger;

    private bool _loadedOnce;
    private Task? _loadTask;
    private readonly object _refreshLock = new();
    private CancellationTokenSource? _refreshCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(ShowUnavailable))]
    [NotifyPropertyChangedFor(nameof(CompactHint))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyPropertyChangedFor(nameof(ShowUnavailable))]
    [NotifyPropertyChangedFor(nameof(CompactHint))]
    private bool _isAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUnavailable))]
    [NotifyPropertyChangedFor(nameof(CompactHint))]
    private string? _unavailableReason;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusCaption))]
    private string? _statusText;

    [ObservableProperty]
    private bool _pruneAllVolumes;

    public DockerCompactViewModel Compact { get; }

    public ObservableCollection<DockerBucketViewModel> Buckets { get; } = [];

    public ObservableCollection<DockerGroupViewModel> Groups { get; } = [];

    public bool CanRun => !IsBusy && IsAvailable;

    public bool ShowUnavailable => !IsBusy && !IsAvailable && !string.IsNullOrWhiteSpace(UnavailableReason);

    public bool IsIdle => !IsBusy;

    public string CompactHint => GetCompactHint();

    public string? TotalsText => Buckets.Count == 0
        ? null
        : $"Итого по категориям: {SizeFormatter.Format(Buckets.Sum(bucket => bucket.SizeBytes))} · можно вернуть по оценке Docker: {SizeFormatter.Format(Buckets.Sum(bucket => bucket.ReclaimBytes))}";

    public string BuildCacheCaption => ReclaimCaption("Кэш сборки", "Build Cache");
    public string ContainersCaption => ReclaimCaption("Остановленные контейнеры", "Containers");

    public string PageTitle => "Docker";

    public string PageDescription => "Сколько места занял Docker и как его вернуть. Очистка безвозвратна – мимо корзины.";

    public string? RefreshTooltip => "Опросить Docker заново";

    public string? StatusCaption => StatusText;

    public bool IsIndeterminate => true;

    public double ProgressValue => 0;

    public double ProgressMax => 1;

    public ICommand? CancelCommand
    {
        get
        {
            lock (_refreshLock)
            {
                return Compact.CanCancel ? Compact.CancelCommand
                    : _refreshCts is not null ? CancelRefreshCommand : null;
            }
        }
    }

    ICommand IPageRefresh.RefreshCommand => RefreshCommand;

    public DockerViewModel(DockerService docker, IDialogService dialogs, IUiDispatcher uiDispatcher, ILogger<DockerViewModel> logger)
    {
        _docker = docker;
        _dialogs = dialogs;
        _uiDispatcher = uiDispatcher;
        _logger = logger;

        Compact = new(this, docker, dialogs, uiDispatcher, logger);
        Buckets.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TotalsText));
            OnPropertyChanged(nameof(BuildCacheCaption));
            OnPropertyChanged(nameof(ContainersCaption));
        };
    }

    private string GetCompactHint()
    {
        if (!Compact.IsElevated)
        {
            return "Для сжатия диска Docker требуются права администратора";
        }

        if (IsBusy)
        {
            return "Дождитесь завершения текущей операции Docker";
        }

        if (!IsAvailable)
        {
            return string.IsNullOrWhiteSpace(UnavailableReason)
                ? "Опрос Docker не выполнен – нажмите «Обновить»"
                : "Docker недоступен";
        }

        return "Остановить Docker Desktop и сжать образ VHDX через diskpart";
    }

    public Task EnsureLoadedAsync()
    {
        lock (_refreshLock)
        {
            if (!_loadedOnce && !IsBusy && _refreshCts is null)
            {
                _loadedOnce = true;
                _loadTask = RefreshAsync();
            }

            return _loadTask ?? Task.CompletedTask;
        }
    }

    internal void NotifyCancelChanged()
    {
        OnPropertyChanged(nameof(CancelCommand));
    }

    internal void ForgetSnapshot()
    {
        lock (_refreshLock)
        {
            IsAvailable = false;
            UnavailableReason = null;
            _loadedOnce = false;
            _loadTask = null;
        }
        Buckets.Clear();
        Groups.Clear();
    }

    private string ReclaimCaption(string title, string type)
    {
        var bucket = Buckets.FirstOrDefault(bucket => bucket.Type == type);
        return bucket is null ? title : $"{title} · ≈{SizeFormatter.Format(bucket.ReclaimBytes)}";
    }

    private static string Summarize(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length > 0 ? lines[^1] : "–";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        CancellationTokenSource cancellation;
        lock (_refreshLock)
        {
            if (IsBusy || _refreshCts is not null)
            {
                return;
            }

            cancellation = new();
            _refreshCts = cancellation;
        }

        try
        {
            _uiDispatcher.Invoke(() =>
            {
                IsBusy = true;
                NotifyCancelChanged();
                StatusText = "Измеряю файл данных Docker…";
            });
            await Compact.RefreshDiskUsageAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            _uiDispatcher.Invoke(() => StatusText = PollingStatus);
            var snapshot = await _docker.GetSnapshotAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            IReadOnlyList<DockerObject> inventory = [];
            if (snapshot.Available)
            {
                _uiDispatcher.Invoke(() => StatusText = "Получаю образы, контейнеры и тома Docker…");
                inventory = await _docker.GetInventoryAsync(cancellation.Token);
            }

            cancellation.Token.ThrowIfCancellationRequested();
            _uiDispatcher.Invoke(() => ApplySnapshot(snapshot, inventory));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            lock (_refreshLock)
            {
                if (!IsAvailable)
                {
                    _loadedOnce = false;
                    _loadTask = null;
                }
            }
            _uiDispatcher.Invoke(() =>
            {
                StatusText = "Опрос Docker остановлен. Нажмите «Обновить», чтобы повторить.";
                if (!IsAvailable)
                {
                    UnavailableReason = null;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.DockerUnavailable(ex.Message);
            _uiDispatcher.Invoke(() => ApplyUnavailable(ex.Message));
        }
        finally
        {
            lock (_refreshLock)
            {
                _refreshCts = null;
                cancellation.Dispose();
            }
            _uiDispatcher.Invoke(() =>
            {
                IsBusy = false;
                NotifyCancelChanged();
            });
        }
    }

    [RelayCommand]
    private void CancelRefresh()
    {
        lock (_refreshLock)
        {
            _refreshCts?.Cancel();
        }
    }

    private void ApplySnapshot(DockerSnapshot snapshot, IReadOnlyList<DockerObject> inventory)
    {
        Buckets.Clear();
        IsAvailable = snapshot.Available;
        UnavailableReason = snapshot.Error;

        Groups.Clear();

        if (!snapshot.Available)
        {
            _logger.DockerUnavailable(snapshot.Error ?? "неизвестно");
            StatusText = "Docker недоступен.";

            return;
        }

        FillBuckets(snapshot.Buckets);
        FillGroups(inventory);

        _logger.DockerSnapshotLoaded(snapshot.Buckets.Count);
        StatusText = $"Обновлено: категорий – {snapshot.Buckets.Count}.";
    }

    private void ApplyUnavailable(string reason)
    {
        IsAvailable = false;
        UnavailableReason = reason;
        Buckets.Clear();
        Groups.Clear();
        StatusText = "Docker недоступен.";
    }

    private void FillGroups(IReadOnlyList<DockerObject> inventory)
    {
        var groups = inventory
            .GroupBy(o => o.Kind)
            .Select(g => new DockerGroupViewModel(g.Key, g.ToList()))
            .OrderByDescending(g => g.TotalBytes)
            .ToList();

        foreach (var group in groups)
        {
            Groups.Add(group);
        }

        _logger.DockerInventoryLoaded(inventory.Count, groups.Count);
    }

    [RelayCommand]
    private async Task RemoveObject(DockerObjectViewModel? row)
    {
        if (row is null || !CanRun || !row.CanRemove)
        {
            return;
        }

        var target = row.Model;
        var kind = target.Kind switch
        {
            DockerObjectKind.Image => "образ",
            DockerObjectKind.Container => "контейнер",
            DockerObjectKind.Volume => "том",
            _ => "объект",
        };

        row.ConfirmingDelete = false;
        IsBusy = true;
        StatusText = $"Удаляю {kind} «{target.Name}»…";
        try
        {
            await _docker.RemoveAsync(target, CancellationToken.None);
            _logger.DockerObjectRemoved(target.Kind.ToString(), target.Name);
        }
        catch (Exception ex)
        {
            _logger.DockerObjectRemoveFailed(ex, target.Kind.ToString(), target.Name);
            _uiDispatcher.Invoke(() =>
            {
                _dialogs.Error($"Удалить {kind}", ex.Message);
                IsBusy = false;
            });

            return;
        }

        _uiDispatcher.Invoke(() => ApplyRemoved(row, kind, target.Name));

        await RefreshBucketsAsync();
    }

    private void ApplyRemoved(DockerObjectViewModel row, string kind, string name)
    {
        foreach (var group in Groups)
        {
            if (group.Remove(row))
            {
                if (group.Count == 0)
                {
                    Groups.Remove(group);
                }

                break;
            }
        }

        IsBusy = false;
        StatusText = $"Удалён {kind} «{name}».";
    }

    private async Task RefreshBucketsAsync()
    {
        try
        {
            var snapshot = await _docker.GetSnapshotAsync(CancellationToken.None);
            if (!snapshot.Available)
            {
                return;
            }

            _uiDispatcher.Invoke(() => FillBuckets(snapshot.Buckets));
        }
        catch (Exception ex)
        {
            _logger.DockerUnavailable(ex.Message);
        }
    }

    private void FillBuckets(IReadOnlyList<DockerUsage> buckets)
    {
        Buckets.Clear();

        foreach (var bucket in DockerBucketViewModel.Build(buckets))
        {
            Buckets.Add(bucket);
        }
    }
}
