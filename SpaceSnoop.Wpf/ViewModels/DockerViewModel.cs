using KeepShell.Services;
using System.Collections.ObjectModel;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class DockerViewModel(
    DockerService docker,
    IDialogService dialogs,
    ILogger<DockerViewModel> logger)
    : ObservableObject, IPageHeader
{
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
    private string? _statusText;

    public ObservableCollection<DockerUsage> Buckets { get; } = [];

    public ObservableCollection<DockerGroupViewModel> Groups { get; } = [];

    public bool CanRun => !IsBusy && IsAvailable;

    public bool IsIdle => !IsBusy;

    public string PageTitle => "Docker";

    public string PageDescription => "Сколько места занял Docker и как его вернуть. Очистка безвозвратна — мимо корзины.";

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
        return lines.Length > 0 ? lines[^1] : "—";
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
                StatusText = $"Обновлено: категорий — {snapshot.Buckets.Count}.";
            }
            else
            {
                logger.DockerUnavailable(snapshot.Error ?? "неизвестно");
                StatusText = "Docker недоступен.";
            }
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

        var warning = target.Kind == DockerObjectKind.Volume
            ? " В томе могут лежать данные (БД и т.п.) — они пропадут БЕЗВОЗВРАТНО."
            : target.InUse
                ? " Объект используется — Docker может отказать в удалении."
                : string.Empty;

        if (!dialogs.Confirm($"Удалить {kind}", $"Удалить {kind} «{target.Name}» ({target.Size})?{warning}"))
        {
            return;
        }

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

        IsBusy = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private Task PruneBuildCache()
    {
        return RunCleanupAsync(DockerCleanupTarget.BuildCache,
            "Очистить кэш сборки",
            "Удалить весь кэш сборки Docker? Это безопасно, но следующая сборка займёт больше времени.");
    }

    [RelayCommand]
    private Task PruneDanglingImages()
    {
        return RunCleanupAsync(DockerCleanupTarget.DanglingImages,
            "Удалить «висячие» образы",
            "Удалить образы без тегов (dangling)? Безвозвратно.");
    }

    [RelayCommand]
    private Task PruneStoppedContainers()
    {
        return RunCleanupAsync(DockerCleanupTarget.StoppedContainers,
            "Удалить остановленные контейнеры",
            "Удалить все остановленные контейнеры? Безвозвратно.");
    }

    [RelayCommand]
    private Task PruneUnusedImages()
    {
        return RunCleanupAsync(DockerCleanupTarget.UnusedImages,
            "Удалить неиспользуемые образы",
            "Удалить ВСЕ образы, не привязанные к контейнерам? Их придётся скачивать заново. Безвозвратно.");
    }

    [RelayCommand]
    private Task PruneVolumes()
    {
        return RunCleanupAsync(DockerCleanupTarget.UnusedVolumes,
            "Удалить неиспользуемые тома",
            "⚠ Удалить неиспользуемые тома? В них лежат данные (БД и т.п.) — они пропадут БЕЗВОЗВРАТНО. Продолжить?");
    }

    [RelayCommand]
    private async Task Compact()
    {
        if (!CanRun
            || !dialogs.Confirm("Сжать диск Docker",
                "WSL и Docker будут остановлены, образ диска (VHDX) сожмётся, место вернётся на диск. "
                + "После этого запустите Docker заново. Продолжить?"))
        {
            return;
        }

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

    private async Task RunCleanupAsync(DockerCleanupTarget target, string title, string confirm)
    {
        if (!CanRun || !dialogs.Confirm(title, confirm))
        {
            return;
        }

        IsBusy = true;
        StatusText = $"{title}…";
        try
        {
            logger.DockerCleanupStarted(target.ToString());
            var result = await docker.PruneAsync(target);
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
