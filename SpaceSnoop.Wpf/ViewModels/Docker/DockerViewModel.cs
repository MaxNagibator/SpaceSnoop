using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Docker;

public sealed partial class DockerViewModel(
    DockerService docker,
    IDialogService dialogs,
    ILogger<DockerViewModel> logger)
    : ObservableObject, IPageHeader, IPageRefresh, IPageStatus
{
    private bool _loadedOnce;
    private CancellationTokenSource? _compactCts;
    private bool _compactCancellationAllowed;

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

    public ObservableCollection<DockerUsage> Buckets { get; } = [];

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

    public ICommand? CancelCommand => _compactCts is null || !_compactCancellationAllowed ? null : CancelCompactCommand;

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

    [RelayCommand]
    private async Task CompactAsync()
    {
        if (!CanRun)
        {
            return;
        }

        var confirm = new ConfirmDialogViewModel(
            "Сжать диск Docker",
            PackIconLucideKind.Shrink,
            [
                "WSL и Docker будут остановлены.",
                "Образ данных (VHDX) сожмётся, освободившееся место вернётся системе.",
                "Операция может занять много времени. После неё Docker нужно запустить заново.",
                "Перед операцией рекомендуется сделать резервную копию данных Docker.",
            ],
            [
                new("Отмена", ConfirmChoiceKind.Dismissive),
                new("Сжать диск", ConfirmChoiceKind.Primary),
            ])
        {
            Warning = "Экспериментальная операция. После запуска diskpart сжатие нельзя безопасно прервать.",
        };

        if (!await dialogs.ShowAsync(confirm))
        {
            return;
        }

        await CompactCoreAsync();
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

        if (!await dialogs.ShowAsync(confirm))
        {
            return;
        }

        await run();
    }

    private async Task CompactCoreAsync()
    {
        _compactCts = new();
        _compactCancellationAllowed = true;
        OnPropertyChanged(nameof(CancelCommand));
        IsBusy = true;
        StatusText = "Сжимаю образ диска Docker…";
        var dockerStopped = false;
        try
        {
            logger.DockerCompactStarted();
            var uiContext = SynchronizationContext.Current;

            void AnnounceCompacting()
            {
                StatusText = "Сжимаю образ disk\\docker_data.vhdx… Отмена недоступна после запуска diskpart.";
                OnPropertyChanged(nameof(CancelCommand));
            }

            void StageChanged(DockerCompactStage stage)
            {
                if (stage != DockerCompactStage.Compacting)
                {
                    return;
                }

                dockerStopped = true;
                _compactCancellationAllowed = false;

                if (uiContext is null)
                {
                    AnnounceCompacting();
                }
                else
                {
                    uiContext.Post(_ => AnnounceCompacting(), null);
                }
            }

            var result = await docker.CompactAsync(_compactCts.Token, stageChanged: StageChanged);
            dockerStopped = result.DockerStopped;

            if (result.RequiresWslShutdownConfirmation)
            {
                if (!await ConfirmWslShutdownFallbackAsync(result))
                {
                    StatusText = result.DockerStopped
                        ? "Сжатие отменено. Docker остановлен – запустите его заново."
                        : "Сжатие отменено.";
                    return;
                }

                var fallbackResult = await docker.CompactAsync(
                    _compactCts.Token,
                    allowWslShutdownFallback: true,
                    stageChanged: StageChanged);
                var attempts = new List<DockerCompactStep>(result.Steps)
                {
                    new("Повтор после подтверждения остановки WSL", true, "Повторена операция с подтверждённым fallback."),
                };
                attempts.AddRange(fallbackResult.Steps);
                result = fallbackResult with { Steps = attempts };
                dockerStopped = result.DockerStopped;
            }

            if (result.Status == DockerCompactStatus.Canceled)
            {
                logger.DockerCompactCancelled();
                StatusText = result.DockerStopped
                    ? "Сжатие отменено. Docker остановлен – запустите его заново."
                    : "Сжатие отменено.";
                return;
            }

            if (!result.Succeeded)
            {
                var failure = new InvalidOperationException(result.Summary);
                logger.DockerCompactFailed(failure);
                dialogs.Error("Сжатие диска Docker", result.ToDisplayText());
                StatusText = result.DockerStopped
                    ? "Сжатие не выполнено. Docker остановлен – запустите его заново."
                    : "Сжатие не выполнено.";
                return;
            }

            logger.DockerCompactFinished(result.Summary);
            dialogs.Info("Сжатие диска Docker", result.ToDisplayText());
            StatusText = "Готово. Запустите Docker заново.";
            Buckets.Clear();
            Groups.Clear();
            _loadedOnce = false;
        }
        catch (OperationCanceledException)
        {
            logger.DockerCompactCancelled();
            StatusText = dockerStopped
                ? "Сжатие отменено. Docker остановлен – запустите его заново."
                : "Сжатие отменено.";
        }
        catch (Exception ex)
        {
            logger.DockerCompactFailed(ex);
            dialogs.Error("Сжатие диска Docker", ex.Message);
            StatusText = dockerStopped
                ? "Сжатие не выполнено. Docker остановлен – запустите его заново."
                : "Сжатие не выполнено.";
        }
        finally
        {
            IsBusy = false;
            _compactCancellationAllowed = false;
            _compactCts.Dispose();
            _compactCts = null;
            OnPropertyChanged(nameof(CancelCommand));
        }
    }

    [RelayCommand]
    private void CancelCompact()
    {
        _compactCts?.Cancel();
    }

    private async Task<bool> ConfirmWslShutdownFallbackAsync(DockerCompactResult result)
    {
        var distributions = result.WslDistributions.Count > 0
            ? string.Join(", ", result.WslDistributions)
            : "дистрибутивы WSL";

        var confirm = new ConfirmDialogViewModel(
            "Остановить WSL целиком?",
            PackIconLucideKind.TriangleAlert,
            [
                "Docker Desktop не удалось остановить штатно.",
                "Fallback выполнит wsl --shutdown и остановит все перечисленные дистрибутивы.",
                $"Будут остановлены: {distributions}.",
            ],
            [
                new("Отмена", ConfirmChoiceKind.Dismissive),
                new("Остановить WSL", ConfirmChoiceKind.Destructive),
            ])
        {
            Warning = "Другие работающие WSL-дистрибутивы тоже будут остановлены.",
        };

        return await dialogs.ShowAsync(confirm);
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
