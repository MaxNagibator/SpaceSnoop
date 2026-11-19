using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class DotNetCleanViewModel : ObservableObject
{
    private readonly DotNetCleanService _service;
    private readonly IDialogService _dialogs;
    private readonly IFilePicker _picker;
    private readonly IShellLauncher _shell;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<DotNetCleanViewModel> _logger;
    private readonly CleanupOperation _operation = new();
    private DotNetCleanPlan? _plan;
    private long _inputVersion;
    private bool _isHostBusy;
    private bool _isBusy;
    private bool _isTrusted;
    private string _projectPath = string.Empty;
    private string _configuration = "Debug";
    private string _framework = string.Empty;
    private string _runtime = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    private DotNetCleanDisplay? _displayPlan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyPropertyChangedFor(nameof(ErrorDetails))]
    private string? _resultText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLogCommand))]
    private string? _logPath;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorDetails))]
    private bool _hasError;

    public string ProjectPath
    {
        get => _projectPath;
        set
        {
            if (SetProperty(ref _projectPath, value ?? string.Empty))
            {
                IsTrusted = false;
                InputChanged();
            }
        }
    }

    public string Configuration
    {
        get => _configuration;
        set
        {
            if (SetProperty(ref _configuration, value ?? string.Empty))
            {
                InputChanged();
            }
        }
    }

    public string Framework
    {
        get => _framework;
        set
        {
            if (SetProperty(ref _framework, value ?? string.Empty))
            {
                InputChanged();
            }
        }
    }

    public string Runtime
    {
        get => _runtime;
        set
        {
            if (SetProperty(ref _runtime, value ?? string.Empty))
            {
                InputChanged();
            }
        }
    }

    public bool IsTrusted
    {
        get => _isTrusted;
        set
        {
            if (SetProperty(ref _isTrusted, value))
            {
                InputChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                NotifyCommands();
            }
        }
    }

    public bool IsHostBusy
    {
        get => _isHostBusy;
        set
        {
            if (SetProperty(ref _isHostBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                NotifyCommands();
            }
        }
    }

    public bool IsIdle => !IsBusy && !IsHostBusy;
    public bool HasPlan => DisplayPlan is not null;
    public bool HasResult => !string.IsNullOrEmpty(ResultText);
    public bool CanClean => IsIdle && IsTrusted && _plan is not null;
    public bool CanCancel => _operation.CanCancel;
    public ICommand? CancelCommand => CanCancel ? CancelOperationCommand : null;

    public string ErrorDetails
    {
        get
        {
            if (!HasError)
            {
                return string.Empty;
            }

            var lines = (ResultText ?? string.Empty).Split('\n')
                .Where(line => line.Contains(": error ", StringComparison.OrdinalIgnoreCase)
                    || line.TrimStart().StartsWith("error ", StringComparison.OrdinalIgnoreCase)
                    || line.Contains(": ошибка ", StringComparison.OrdinalIgnoreCase))
                .Take(8)
                .ToArray();

            return lines.Length > 0
                ? string.Join(Environment.NewLine, lines)
                : "Причина сбоя не найдена в показанном конце вывода. Откройте полный журнал.";
        }
    }

    public DotNetCleanViewModel(
        DotNetCleanService service,
        IDialogService dialogs,
        IFilePicker picker,
        IShellLauncher shell,
        IUiDispatcher dispatcher,
        ILogger<DotNetCleanViewModel> logger)
    {
        _service = service;
        _dialogs = dialogs;
        _picker = picker;
        _shell = shell;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    partial void OnDisplayPlanChanged(DotNetCleanDisplay? value)
    {
        OnPropertyChanged(nameof(CanClean));
    }

    private void InputChanged()
    {
        _inputVersion++;
        _plan = null;
        DisplayPlan = null;
        ResultText = null;
        HasError = false;
        LogPath = null;
        StatusText = "Параметры изменились. Повторите проверку.";
        OnPropertyChanged(nameof(CanClean));
        NotifyCommands();
    }

    [RelayCommand(CanExecute = nameof(CanChooseProject))]
    private void PickProject()
    {
        if (!CanChooseProject())
        {
            return;
        }

        var path = _picker.PickFile(new("Выберите проект .NET", "Проекты .NET (*.csproj;*.fsproj;*.vbproj)|*.csproj;*.fsproj;*.vbproj|Все файлы (*.*)|*.*"));
        if (path is not null)
        {
            ProjectPath = path;
        }
    }

    private bool CanChooseProject()
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

        var version = _inputVersion;
        var project = ProjectPath;
        var options = GetOptions();
        _operation.Begin(value => IsBusy = value);
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CancelCommand));
        _plan = null;
        DisplayPlan = null;
        ResultText = null;
        LogPath = null;
        StatusText = "Проверяю SDK в каталоге проекта…";
        HasError = false;
        try
        {
            var plan = await _service.InspectAsync(project, options, IsTrusted, _operation.Token);
            _dispatcher.Invoke(() =>
            {
                if (version != _inputVersion || !IsTrusted || project != ProjectPath || options != GetOptions())
                {
                    return;
                }

                _plan = plan;
                DisplayPlan = new(
                    plan.ProjectPath,
                    plan.Executable,
                    plan.WorkingDirectory,
                    plan.SdkVersion,
                    plan.Options.Configuration,
                    plan.Options.Framework ?? "",
                    plan.Options.Runtime ?? "",
                    plan.Command);

                LogPath = plan.InspectionLogPath;
                StatusText = "Проверка завершена.";
                OnPropertyChanged(nameof(CanClean));
            });
        }
        catch (OperationCanceledException)
        {
            StatusText = "Проверка отменена.";
        }
        catch (DotNetCleanInspectionException exception)
        {
            HasError = exception.Outcome != DotNetCliOutcome.Cancelled;
            LogPath = exception.LogPath;
            ResultText = FormatOutput(exception.OutputTail, exception.OutputTruncated, exception.Message);
            StatusText = exception.Outcome == DotNetCliOutcome.Cancelled ? "Проверка отменена." : exception.Message;
            if (exception.Outcome != DotNetCliOutcome.Cancelled)
            {
                _logger.DotNetCleanFailed(exception);
            }
        }
        catch (Exception exception)
        {
            HasError = true;
            _logger.DotNetCleanFailed(exception);
            StatusText = exception.Message;
            ResultText = exception.Message;
        }
        finally
        {
            _operation.End();
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CancelCommand));
        }
    }

    private bool CanInspect()
    {
        return IsIdle && IsTrusted && !string.IsNullOrWhiteSpace(ProjectPath);
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        if (!CanClean || _plan is null)
        {
            return;
        }

        var plan = _plan;
        var version = _inputVersion;
        var project = ProjectPath;
        var confirmation = new ConfirmDialogViewModel(
            "Очистка проекта .NET",
            PackIconLucideKind.Trash2,
            [
                new ConfirmTextLine("Будет выполнена команда: " + plan.Command, ConfirmTextTone.Strong),
                new ConfirmTextLine("Проект: " + plan.ProjectPath),
                new ConfirmTextLine("Исполняемый файл: " + plan.Executable),
                new ConfirmTextLine("Рабочий каталог: " + plan.WorkingDirectory),
                new ConfirmTextLine("Активный SDK: " + plan.SdkVersion),
                new ConfirmTextLine(
                    $"Параметры: configuration={plan.Options.Configuration}, framework={plan.Options.Framework ?? "(нет)"}, runtime={plan.Options.Runtime ?? "(нет)"}"),
                new ConfirmTextLine("Очистка необратима, может завершиться частично и не имеет отката или пробного запуска.", ConfirmTextTone.Strong),
                new ConfirmTextLine("Отмена останавливает только процесс dotnet и его дочерние процессы. Уже удалённые файлы не восстанавливаются."),
            ],
            [new("Отмена", ConfirmChoiceKind.Dismissive), new("Выполнить dotnet clean", ConfirmChoiceKind.Destructive)])
        {
            Warning = "Проверьте проект, SDK и точную команду. Импортированные задачи могут менять данные вне каталога проекта.",
        };

        _operation.Begin(value => IsBusy = value);
        try
        {
            if (!await _operation.ConfirmAsync(_dialogs, confirmation))
            {
                return;
            }

            if (!ReferenceEquals(_plan, plan)
                || version != _inputVersion
                || !IsTrusted
                || ProjectPath != project
                || GetOptions() != plan.Options
                || DateTime.UtcNow > plan.ExpiresUtc)
            {
                _plan = null;
                DisplayPlan = null;
                StatusText = "План изменился или устарел. Повторите проверку.";
                return;
            }

            _plan = null;
            OnPropertyChanged(nameof(CanClean));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CancelCommand));
            StatusText = "Выполняю dotnet clean…";
            HasError = false;
            var report = await _service.CleanAsync(plan, IsTrusted, _operation.Token);
            _dispatcher.Invoke(() => ApplyReport(report));
            _logger.DotNetCleanFinished(report.Outcome.ToString(), report.ExitCode, report.LogPath);
        }
        catch (Exception exception)
        {
            HasError = true;
            _logger.DotNetCleanFailed(exception);
            StatusText = exception.Message;
            ResultText = exception.Message;
        }
        finally
        {
            _operation.End();
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CancelCommand));
        }
    }

    private void ApplyReport(DotNetCleanReport report)
    {
        HasError = !report.Succeeded && !report.Cancelled;
        var outcome = CleanupOutcomeText.For(report.Outcome);

        StatusText = outcome + ".";
        ResultText = outcome
            + (report.ExitCode is { } code ? $". Код завершения: {code}." : ".")
            + (report.Error is null ? string.Empty : Environment.NewLine + report.Error)
            + (report.OutputTail.Length == 0 ? string.Empty : Environment.NewLine + report.OutputTail)
            + (report.OutputTruncated ? Environment.NewLine + "В окне показан только конец вывода." : string.Empty)
            + (report.Cancelled || !report.Succeeded ? Environment.NewLine + "Результат может быть частичным; уже удалённые файлы не восстановлены." : string.Empty);

        if (!string.IsNullOrEmpty(report.LogPath))
        {
            LogPath = report.LogPath;
        }
    }

    private static string FormatOutput(string tail, bool truncated, string message)
    {
        return message
            + (tail.Length == 0 ? string.Empty : Environment.NewLine + tail)
            + (truncated ? Environment.NewLine + "В окне показан только конец вывода." : string.Empty);
    }

    private DotNetCleanOptions GetOptions()
    {
        return new(Configuration.Trim(), EmptyToNull(Framework), EmptyToNull(Runtime));
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [RelayCommand(CanExecute = nameof(CanOpenLog))]
    private void OpenLog()
    {
        if (CanOpenLog())
        {
            _shell.Reveal(LogPath!);
        }
    }

    private bool CanOpenLog()
    {
        return !string.IsNullOrWhiteSpace(LogPath);
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _operation.Cancel();
    }

    private void NotifyCommands()
    {
        PickProjectCommand.NotifyCanExecuteChanged();
        InspectCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
        OpenLogCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanClean));
        OnPropertyChanged(nameof(CancelCommand));
    }
}

public sealed record DotNetCleanDisplay(
    string ProjectPath,
    string Executable,
    string WorkingDirectory,
    string SdkVersion,
    string Configuration,
    string Framework,
    string Runtime,
    string Command);
