using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Docker;

public sealed partial class DockerViewModel
{
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
            _uiDispatcher.Invoke(() => _dialogs.Info(title, string.IsNullOrWhiteSpace(result) ? "Готово. Освобождать было нечего." : result));
        }
        catch (Exception ex)
        {
            _logger.DockerCleanupFailed(ex, target.ToString());
            _uiDispatcher.Invoke(() =>
            {
                _dialogs.Error(title, ex.Message);
                IsBusy = false;
            });

            return;
        }

        IsBusy = false;
        await RefreshAsync();
    }
}
