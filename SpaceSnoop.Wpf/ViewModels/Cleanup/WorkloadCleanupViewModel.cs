using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class WorkloadCleanupViewModel : ObservableObject
{
    private readonly WorkloadCleanupService _service;
    private readonly IDialogService _dialogs;
    private readonly IShellLauncher _shell;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<WorkloadCleanupViewModel> _logger;
    private readonly CleanupOperation _operation = new();
    private WorkloadCleanupPlan? _plan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(InspectCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(InspectCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _isHostBusy;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _resultText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    private WorkloadContext? _displayPlan;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLogCommand))]
    private string? _logPath;

    public bool IsIdle => !IsBusy && !IsHostBusy;
    public bool HasPlan => DisplayPlan is not null;
    public bool HasResult => !string.IsNullOrEmpty(ResultText);
    public ICommand? CancelCommand => IsBusy ? CancelOperationCommand : null;

    private bool CanClean => IsIdle && _plan is not null;

    public WorkloadCleanupViewModel(
        WorkloadCleanupService service,
        IDialogService dialogs,
        IShellLauncher shell,
        IUiDispatcher dispatcher,
        ILogger<WorkloadCleanupViewModel> logger)
    {
        _service = service;
        _dialogs = dialogs;
        _shell = shell;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    private bool CanInspect()
    {
        return IsIdle;
    }

    [RelayCommand(CanExecute = nameof(CanInspect))]
    private async Task InspectAsync()
    {
        if (!CanInspect())
        {
            return;
        }

        _operation.Begin(value => IsBusy = value);
        _plan = null;
        DisplayPlan = null;
        LogPath = null;
        ResultText = null;
        StatusText = "Определяю SDK и установленные workloads…";
        try
        {
            var plan = await _service.InspectAsync(_operation.Token);
            _dispatcher.Invoke(() =>
            {
                _plan = plan;
                DisplayPlan = new(
                    plan.Executable,
                    plan.WorkingDirectory,
                    plan.SdkVersion,
                    plan.InstalledWorkloads,
                    plan.Command);

                LogPath = plan.WorkloadListLogPath;
                StatusText = "Проверка завершена.";
            });
        }
        catch (OperationCanceledException)
        {
            StatusText = "Проверка отменена.";
        }
        catch (WorkloadInspectionException exception) when (exception.Outcome == DotNetCliOutcome.Cancelled)
        {
            LogPath = exception.LogPath;
            ResultText = exception.OutputTail.Length == 0 ? null : exception.OutputTail;
            StatusText = "Проверка отменена.";
        }
        catch (Exception exception)
        {
            _logger.WorkloadCleanupFailed(exception);
            if (exception is WorkloadInspectionException inspection)
            {
                LogPath = inspection.LogPath;
                ResultText = exception.Message + Environment.NewLine + inspection.OutputTail;
            }

            StatusText = exception.Message;
        }
        finally
        {
            _operation.End();
        }
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        if (!CanClean || _plan is null)
        {
            return;
        }

        var plan = _plan;
        var confirmation = new ConfirmDialogViewModel(
            "Очистка workloads .NET",
            PackIconLucideKind.Trash2,
            [
                new ConfirmTextLine("Будет выполнена команда: " + plan.Command, ConfirmTextTone.Strong),
                new ConfirmTextLine("Исполняемый файл: " + plan.Executable),
                new ConfirmTextLine("Рабочий каталог: " + plan.WorkingDirectory),
                new ConfirmTextLine("Активный SDK: " + plan.SdkVersion),
                new ConfirmTextLine("Установленные workloads:" + Environment.NewLine + plan.InstalledWorkloads),
                new ConfirmTextLine(
                    "Удаление необратимо. Точного предварительного просмотра нет, отмена не вернёт уже удалённые данные, откат недоступен; размер освобождённого места не измеряется.",
                    ConfirmTextTone.Strong),
                new ConfirmTextLine("SDK, Runtime, инструменты и проекты не являются выбираемыми целями этой команды."),
            ],
            [new("Отмена", ConfirmChoiceKind.Dismissive), new("Очистить", ConfirmChoiceKind.Destructive)])
        {
            Warning = "Проверьте SDK и список workloads. Не запускайте параллельно установку или обновление SDK и workloads.",
        };

        _operation.Begin(value => IsBusy = value);
        try
        {
            if (!await _operation.ConfirmAsync(_dialogs, confirmation))
            {
                return;
            }

            if (!ReferenceEquals(_plan, plan) || DateTime.UtcNow > plan.ExpiresUtc)
            {
                _plan = null;
                DisplayPlan = null;
                StatusText = "План изменился или устарел. Повторите проверку.";
                return;
            }

            _plan = null;
            StatusText = "Выполняю dotnet workload clean…";
            var report = await _service.CleanAsync(plan, _operation.Token);
            var outcome = CleanupOutcomeText.For(report.Outcome);

            ResultText = outcome
                + (report.ExitCode is { } code ? $". Код завершения: {code}." : ".")
                + (report.Error is null ? string.Empty : Environment.NewLine + report.Error)
                + (report.OutputTail.Length == 0 ? string.Empty : Environment.NewLine + report.OutputTail)
                + (report.OutputTruncated ? Environment.NewLine + "В окне показан только конец вывода." : string.Empty)
                + (report.Cancelled || !report.Succeeded ? Environment.NewLine + "Выполненные изменения не отменены; очистка могла завершиться частично." : string.Empty);

            StatusText = outcome + ".";
            if (!string.IsNullOrEmpty(report.LogPath))
            {
                LogPath = report.LogPath;
            }

            _logger.WorkloadCleanupFinished(report.Outcome.ToString(), report.ExitCode, report.LogPath);
        }
        catch (Exception exception)
        {
            _logger.WorkloadCleanupFailed(exception);
            StatusText = exception.Message;
            ResultText = exception.Message;
        }
        finally
        {
            _operation.End();
        }
    }

    private bool CanOpenLog()
    {
        return !string.IsNullOrWhiteSpace(LogPath);
    }

    [RelayCommand(CanExecute = nameof(CanOpenLog))]
    private void OpenLog()
    {
        if (CanOpenLog())
        {
            _shell.Reveal(LogPath!);
        }
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _operation.Cancel();
    }
}

public sealed record WorkloadContext(
    string Executable,
    string WorkingDirectory,
    string SdkVersion,
    string InstalledWorkloads,
    string Command);
