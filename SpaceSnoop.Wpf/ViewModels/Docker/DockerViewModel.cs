using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Docker;

public sealed partial class DockerViewModel : ObservableObject, IPageHeader, IPageRefresh, IPageStatus
{
    private readonly DockerService _docker;
    private readonly IDialogService _dialogs;
    private readonly ILogger<DockerViewModel> _logger;

    private bool _loadedOnce;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    private bool _isAvailable = true;

    [ObservableProperty]
    private string? _unavailableReason;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusCaption))]
    private string? _statusText;

    [ObservableProperty]
    private bool _pruneAllVolumes;

    public DockerViewModel(DockerService docker, IDialogService dialogs, ILogger<DockerViewModel> logger)
    {
        _docker = docker;
        _dialogs = dialogs;
        _logger = logger;

        Compact = new(this, docker, dialogs, logger);
    }

    public DockerCompactViewModel Compact { get; }

    public ObservableCollection<DockerBucketViewModel> Buckets { get; } = [];

    public ObservableCollection<DockerGroupViewModel> Groups { get; } = [];

    public bool CanRun => !IsBusy && IsAvailable;

    public bool IsIdle => !IsBusy;

    public string PageTitle => "Docker";

    public string PageDescription => "Сколько места занял Docker и как его вернуть. Очистка безвозвратна – мимо корзины.";

    public string? RefreshTooltip => "Опросить Docker заново";

    public string? StatusCaption => StatusText;

    public bool IsIndeterminate => true;

    public double ProgressValue => 0;

    public double ProgressMax => 1;

    public ICommand? CancelCommand => Compact.CanCancel ? Compact.CancelCommand : null;

    ICommand IPageRefresh.RefreshCommand => RefreshCommand;

    public async Task EnsureLoadedAsync()
    {
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;
        await RefreshAsync();
    }

    internal void NotifyCancelChanged()
    {
        OnPropertyChanged(nameof(CancelCommand));
    }

    internal void ForgetSnapshot()
    {
        Buckets.Clear();
        Groups.Clear();
        _loadedOnce = false;
    }

    private static string Summarize(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length > 0 ? lines[^1] : "–";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Опрашиваю Docker…";
        try
        {
            var snapshot = await _docker.GetSnapshotAsync(CancellationToken.None);

            Buckets.Clear();
            IsAvailable = snapshot.Available;
            UnavailableReason = snapshot.Error;

            Groups.Clear();

            if (snapshot.Available)
            {
                FillBuckets(snapshot.Buckets);

                await LoadObjectsAsync();

                _logger.DockerSnapshotLoaded(snapshot.Buckets.Count);
                StatusText = $"Обновлено: категорий – {snapshot.Buckets.Count}.";
            }
            else
            {
                _logger.DockerUnavailable(snapshot.Error ?? "неизвестно");
                StatusText = "Docker недоступен.";
            }
        }
        catch (Exception ex)
        {
            _logger.DockerUnavailable(ex.Message);
            IsAvailable = false;
            UnavailableReason = ex.Message;
            Buckets.Clear();
            Groups.Clear();
            StatusText = "Docker недоступен.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadObjectsAsync()
    {
        var inventory = await _docker.GetInventoryAsync(CancellationToken.None);

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
        if (row is null || !CanRun)
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
            _dialogs.Error($"Удалить {kind}", ex.Message);
            IsBusy = false;
            return;
        }

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
        StatusText = $"Удалён {kind} «{target.Name}».";
        await RefreshBucketsAsync();
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

            FillBuckets(snapshot.Buckets);
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

    [RelayCommand]
    private Task PruneBuildCacheAsync()
    {
        return ConfirmCleanupAsync(
            "Кэш сборки",
            PackIconLucideKind.Hammer,
            ["Будет удалён весь кэш сборки Docker.", "Следующая сборка займёт больше времени."],
            "Очистить кэш",
            () => RunCleanupAsync(DockerCleanupTarget.BuildCache, "Очистить кэш сборки"));
    }

    [RelayCommand]
    private Task PruneDanglingImagesAsync()
    {
        return ConfirmCleanupAsync(
            "Висячие образы",
            PackIconLucideKind.Image,
            ["Будут удалены образы без тегов (dangling).", "Образы, привязанные к контейнерам, не тронуты."],
            "Удалить образы",
            () => RunCleanupAsync(DockerCleanupTarget.DanglingImages, "Удалить «висячие» образы"));
    }

    [RelayCommand]
    private Task PruneStoppedContainersAsync()
    {
        return ConfirmCleanupAsync(
            "Остановленные контейнеры",
            PackIconLucideKind.Box,
            ["Будут удалены все остановленные контейнеры.", "Данные внутри них пропадут вместе с контейнером."],
            "Удалить контейнеры",
            () => RunCleanupAsync(DockerCleanupTarget.StoppedContainers, "Удалить остановленные контейнеры"));
    }

    [RelayCommand]
    private Task PruneUnusedImagesAsync()
    {
        return ConfirmCleanupAsync(
            "Неиспользуемые образы",
            PackIconLucideKind.Trash2,
            ["Будут удалены все образы, не привязанные к контейнерам.", "Нужные придётся скачивать заново."],
            "Удалить образы",
            () => RunCleanupAsync(DockerCleanupTarget.UnusedImages, "Удалить неиспользуемые образы"));
    }

    [RelayCommand]
    private Task PruneVolumesAsync()
    {
        var allUnused = PruneAllVolumes;

        var scope = allUnused
            ? "Будут удалены все неиспользуемые тома, включая именованные."
            : "Будут удалены неиспользуемые анонимные тома.";

        return ConfirmCleanupAsync(
            "Неиспользуемые тома",
            PackIconLucideKind.Database,
            [scope, "В томах лежат данные приложений – базы, кэши, загруженные файлы."],
            "Удалить тома",
            () => RunCleanupAsync(DockerCleanupTarget.UnusedVolumes, "Удалить неиспользуемые тома", allUnused));
    }

    private async Task ConfirmCleanupAsync(
        string title,
        PackIconLucideKind iconKind,
        IReadOnlyList<string> lines,
        string action,
        Func<Task> run)
    {
        if (!CanRun)
        {
            return;
        }

        var confirm = new ConfirmDialogViewModel(
            title,
            iconKind,
            lines,
            [
                new("Отмена", ConfirmChoiceKind.Dismissive),
                new(action, ConfirmChoiceKind.Destructive),
            ])
        {
            Warning = "Docker удаляет мимо корзины – вернуть удалённое нельзя.",
        };

        if (!await _dialogs.ShowAsync(confirm))
        {
            return;
        }

        await run();
    }

    private async Task RunCleanupAsync(DockerCleanupTarget target, string title, bool allUnused = false)
    {
        if (!CanRun)
        {
            return;
        }

        IsBusy = true;
        StatusText = $"{title}…";
        try
        {
            _logger.DockerCleanupStarted(target.ToString());
            var result = await _docker.PruneAsync(target, allUnused, CancellationToken.None);
            _logger.DockerCleanupFinished(target.ToString(), Summarize(result));
            _dialogs.Info(title, string.IsNullOrWhiteSpace(result) ? "Готово. Освобождать было нечего." : result);
        }
        catch (Exception ex)
        {
            _logger.DockerCleanupFailed(ex, target.ToString());
            _dialogs.Error(title, ex.Message);
            IsBusy = false;
            return;
        }

        IsBusy = false;
        await RefreshAsync();
    }
}
