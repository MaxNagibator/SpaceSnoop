using KeepShell.Services;
using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Docker;

public sealed partial class DockerCompactViewModel : ObservableObject
{
    private readonly DockerViewModel _owner;
    private readonly DockerService _docker;
    private readonly IDialogService _dialogs;
    private readonly ILogger _logger;

    private CancellationTokenSource? _cts;
    private bool _cancellationAllowed;

    internal DockerCompactViewModel(DockerViewModel owner, DockerService docker, IDialogService dialogs, ILogger logger)
    {
        _owner = owner;
        _docker = docker;
        _dialogs = dialogs;
        _logger = logger;
    }

    public bool CanCancel => _cts is not null && _cancellationAllowed;

    [RelayCommand]
    private async Task Run()
    {
        if (!_owner.CanRun)
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

        if (!await _dialogs.ShowAsync(confirm))
        {
            return;
        }

        await RunCoreAsync();
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    private async Task RunCoreAsync()
    {
        _cts = new();
        _cancellationAllowed = true;
        _owner.NotifyCancelChanged();
        _owner.IsBusy = true;
        _owner.StatusText = "Сжимаю образ диска Docker…";
        var dockerStopped = false;
        try
        {
            _logger.DockerCompactStarted();
            var uiContext = SynchronizationContext.Current;

            void AnnounceCompacting()
            {
                _owner.StatusText = "Сжимаю образ disk\\docker_data.vhdx… Отмена недоступна после запуска diskpart.";
                _owner.NotifyCancelChanged();
            }

            void StageChanged(DockerCompactStage stage)
            {
                if (stage != DockerCompactStage.Compacting)
                {
                    return;
                }

                dockerStopped = true;
                _cancellationAllowed = false;

                if (uiContext is null)
                {
                    AnnounceCompacting();
                }
                else
                {
                    uiContext.Post(_ => AnnounceCompacting(), null);
                }
            }

            var result = await _docker.CompactAsync(_cts.Token, stageChanged: StageChanged);
            dockerStopped = result.DockerStopped;

            if (result.RequiresWslShutdownConfirmation)
            {
                if (!await ConfirmWslShutdownFallbackAsync(result))
                {
                    _owner.StatusText = result.DockerStopped
                        ? "Сжатие отменено. Docker остановлен – запустите его заново."
                        : "Сжатие отменено.";
                    return;
                }

                var fallbackResult = await _docker.CompactAsync(
                    _cts.Token,
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
                _logger.DockerCompactCancelled();
                _owner.StatusText = result.DockerStopped
                    ? "Сжатие отменено. Docker остановлен – запустите его заново."
                    : "Сжатие отменено.";
                return;
            }

            if (!result.Succeeded)
            {
                var failure = new InvalidOperationException(result.Summary);
                _logger.DockerCompactFailed(failure);
                _dialogs.Error("Сжатие диска Docker", result.ToDisplayText());
                _owner.StatusText = result.DockerStopped
                    ? "Сжатие не выполнено. Docker остановлен – запустите его заново."
                    : "Сжатие не выполнено.";
                return;
            }

            _logger.DockerCompactFinished(result.Summary);
            _dialogs.Info("Сжатие диска Docker", result.ToDisplayText());
            _owner.StatusText = "Готово. Запустите Docker заново.";
            _owner.ForgetSnapshot();
        }
        catch (OperationCanceledException)
        {
            _logger.DockerCompactCancelled();
            _owner.StatusText = dockerStopped
                ? "Сжатие отменено. Docker остановлен – запустите его заново."
                : "Сжатие отменено.";
        }
        catch (Exception ex)
        {
            _logger.DockerCompactFailed(ex);
            _dialogs.Error("Сжатие диска Docker", ex.Message);
            _owner.StatusText = dockerStopped
                ? "Сжатие не выполнено. Docker остановлен – запустите его заново."
                : "Сжатие не выполнено.";
        }
        finally
        {
            _owner.IsBusy = false;
            _cancellationAllowed = false;
            _cts.Dispose();
            _cts = null;
            _owner.NotifyCancelChanged();
        }
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

        return await _dialogs.ShowAsync(confirm);
    }
}
