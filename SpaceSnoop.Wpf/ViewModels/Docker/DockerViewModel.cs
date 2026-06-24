using KeepShell.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Docker;

public sealed partial class DockerViewModel(
    DockerService docker,
    IDialogService dialogs,
    ILogger<DockerViewModel> logger)
    : ObservableObject, IPageHeader, IPageRefresh
{
    private bool _loadedOnce;

    private Func<Task>? _pendingCleanupAction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanArmCleanup))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyPropertyChangedFor(nameof(CanArmCleanup))]
    private bool _isAvailable = true;

    [ObservableProperty]
    private string? _unavailableReason;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private bool _pruneAllVolumes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingCleanup))]
    [NotifyPropertyChangedFor(nameof(CanArmCleanup))]
    private string? _pendingCleanupMessage;

    public ObservableCollection<DockerUsage> Buckets { get; } = [];

    public ObservableCollection<DockerGroupViewModel> Groups { get; } = [];

    public bool CanRun => !IsBusy && IsAvailable;

    public bool CanArmCleanup => CanRun && !HasPendingCleanup;

    public bool HasPendingCleanup => PendingCleanupMessage is not null;

    public bool IsIdle => !IsBusy;

    public string PageTitle => "Docker";

    public string PageDescription => "Сколько места занял Docker и как его вернуть. Очистка безвозвратна – мимо корзины.";

    public string? RefreshTooltip => "Опросить Docker заново";

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
            var snapshot = await docker.GetSnapshotAsync();

            Buckets.Clear();
            IsAvailable = snapshot.Available;
            UnavailableReason = snapshot.Error;

            Groups.Clear();

            if (snapshot.Available)
            {
                foreach (var bucket in snapshot.Buckets)
                {
                    Buckets.Add(bucket);
                }

                await LoadObjectsAsync();

                logger.DockerSnapshotLoaded(snapshot.Buckets.Count);
                StatusText = $"Обновлено: категорий – {snapshot.Buckets.Count}.";
            }
            else
            {
                logger.DockerUnavailable(snapshot.Error ?? "неизвестно");
                StatusText = "Docker недоступен.";
            }
        }
        catch (Exception ex)
        {
            logger.DockerUnavailable(ex.Message);
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
        var inventory = await docker.GetInventoryAsync();

        var groups = inventory
            .GroupBy(o => o.Kind)
            .Select(g => new DockerGroupViewModel(g.Key, g.ToList()))
            .OrderByDescending(g => g.TotalBytes)
            .ToList();

        foreach (var group in groups)
        {
            Groups.Add(group);
        }

        logger.DockerInventoryLoaded(inventory.Count, groups.Count);
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
            await docker.RemoveAsync(target);
            logger.DockerObjectRemoved(target.Kind.ToString(), target.Name);
        }
        catch (Exception ex)
        {
            logger.DockerObjectRemoveFailed(ex, target.Kind.ToString(), target.Name);
            dialogs.Error($"Удалить {kind}", ex.Message);
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
            var snapshot = await docker.GetSnapshotAsync();
            if (!snapshot.Available)
            {
                return;
            }

            Buckets.Clear();
            foreach (var bucket in snapshot.Buckets)
            {
                Buckets.Add(bucket);
            }
        }
        catch (Exception ex)
        {
            logger.DockerUnavailable(ex.Message);
        }
    }

    [RelayCommand]
    private void PruneBuildCache()
    {
        ArmCleanup("Удалить весь кэш сборки Docker? Это безопасно, но следующая сборка займёт больше времени.",
            () => RunCleanupAsync(DockerCleanupTarget.BuildCache, "Очистить кэш сборки"));
    }

    [RelayCommand]
    private void PruneDanglingImages()
    {
        ArmCleanup("Удалить образы без тегов (dangling)? Безвозвратно.",
            () => RunCleanupAsync(DockerCleanupTarget.DanglingImages, "Удалить «висячие» образы"));
    }

    [RelayCommand]
    private void PruneStoppedContainers()
    {
        ArmCleanup("Удалить все остановленные контейнеры? Безвозвратно.",
            () => RunCleanupAsync(DockerCleanupTarget.StoppedContainers, "Удалить остановленные контейнеры"));
    }

    [RelayCommand]
    private void PruneUnusedImages()
    {
        ArmCleanup("Удалить ВСЕ образы, не привязанные к контейнерам? Их придётся скачивать заново. Безвозвратно.",
            () => RunCleanupAsync(DockerCleanupTarget.UnusedImages, "Удалить неиспользуемые образы"));
    }

    [RelayCommand]
    private void PruneVolumes()
    {
        var scope = PruneAllVolumes ? "ВСЕ неиспользуемые тома (включая именованные)" : "неиспользуемые анонимные тома";
        var allUnused = PruneAllVolumes;
        ArmCleanup($"⚠ Удалить {scope}? В них лежат данные (БД и т.п.) – они пропадут БЕЗВОЗВРАТНО.",
            () => RunCleanupAsync(DockerCleanupTarget.UnusedVolumes, "Удалить неиспользуемые тома", allUnused));
    }

    [RelayCommand]
    private void Compact()
    {
        ArmCleanup("WSL и Docker будут остановлены, образ диска (VHDX) сожмётся, место вернётся на диск. "
            + "После этого запустите Docker заново.",
            CompactCoreAsync);
    }

    private void ArmCleanup(string message, Func<Task> action)
    {
        if (!CanRun)
        {
            return;
        }

        _pendingCleanupAction = action;
        PendingCleanupMessage = message;
    }

    [RelayCommand]
    private void CancelCleanup()
    {
        _pendingCleanupAction = null;
        PendingCleanupMessage = null;
    }

    [RelayCommand]
    private async Task ConfirmCleanup()
    {
        var action = _pendingCleanupAction;
        CancelCleanup();
        if (action is not null)
        {
            await action();
        }
    }

    private async Task CompactCoreAsync()
    {
        IsBusy = true;
        StatusText = "Сжимаю образ диска Docker…";
        try
        {
            logger.DockerCompactStarted();
            var result = await docker.CompactAsync();
            logger.DockerCompactFinished();
            dialogs.Info("Сжатие диска Docker", result);
            StatusText = "Готово. Запустите Docker заново.";
            Buckets.Clear();
            Groups.Clear();
            _loadedOnce = false;
        }
        catch (Exception ex)
        {
            logger.DockerCompactFailed(ex);
            dialogs.Error("Сжатие диска Docker", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
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
            logger.DockerCleanupStarted(target.ToString());
            var result = await docker.PruneAsync(target, allUnused);
            logger.DockerCleanupFinished(target.ToString(), Summarize(result));
            dialogs.Info(title, string.IsNullOrWhiteSpace(result) ? "Готово. Освобождать было нечего." : result);
        }
        catch (Exception ex)
        {
            logger.DockerCleanupFailed(ex, target.ToString());
            dialogs.Error(title, ex.Message);
            IsBusy = false;
            return;
        }

        IsBusy = false;
        await RefreshAsync();
    }
}
